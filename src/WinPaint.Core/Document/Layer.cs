using WinPaint.Core.Imaging;
using WinPaint.Core.Text;

namespace WinPaint.Core.Document;

/// <summary>
/// A document layer: an ordered element stack <c>[RasterSegment₀, Text, RasterSegment₁, …]</c> whose top element
/// is always a <see cref="RasterSegment"/> (where new painting goes).
/// </summary>
public sealed class Layer
{
    private readonly List<PixelRect> _dirty = [];
    private TiledSurface? _cache;

    /// <summary>Creates a layer with a single empty segment.</summary>
    public Layer(int width, int height, string name)
    {
        Name = name;
        Elements.Add(new RasterSegment(width, height));
    }

    /// <summary>Creates a layer from explicit elements (used by history restore and layer ops).</summary>
    public Layer(string name, IEnumerable<LayerElement> elements)
    {
        Name = name;
        Elements.AddRange(elements);
        if (Elements.Count == 0 || Elements[0] is not RasterSegment || Elements[^1] is not RasterSegment)
        {
            throw new ArgumentException("A layer stack must start and end with a raster segment.", nameof(elements));
        }
    }

    /// <summary>Stable identity.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; }

    /// <summary>Visibility.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Opacity 0..1.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Blend mode against the layers below.</summary>
    public BlendMode BlendMode { get; set; }

    /// <summary>True for the bottom background layer.</summary>
    public bool IsBackground { get; set; }

    /// <summary>
    /// For the background layer: when false the layer is opaque (eraser paints the secondary color);
    /// when true it is a transparent canvas (eraser erases to transparent). Other layers are always transparent.
    /// </summary>
    public bool IsTransparent { get; set; } = true;

    /// <summary>True when erasing should produce transparency rather than the secondary color.</summary>
    public bool ErasesToTransparent => !IsBackground || IsTransparent;

    /// <summary>Element stack, bottom to top.</summary>
    public List<LayerElement> Elements { get; } = [];

    /// <summary>The segment that receives new painting.</summary>
    public RasterSegment TopSegment => (RasterSegment)Elements[^1];

    /// <summary>Live text objects on this layer, bottom to top.</summary>
    public IEnumerable<TextObject> TextObjects => Elements.OfType<TextObject>();

    /// <summary>Monotonic counter bumped on every content change (for thumbnails).</summary>
    public long Version { get; private set; }

    /// <summary>Marks a region as changed.</summary>
    public void Invalidate(PixelRect r)
    {
        Version++;
        if (r.IsEmpty)
        {
            return;
        }

        for (var i = _dirty.Count - 1; i >= 0; i--)
        {
            if (_dirty[i].IntersectsWith(r.Inflate(1)))
            {
                r = r.Union(_dirty[i]);
                _dirty.RemoveAt(i);
            }
        }

        _dirty.Add(r);
    }

    /// <summary>Discards the composite cache entirely.</summary>
    public void InvalidateAll()
    {
        Version++;
        _cache = null;
        _dirty.Clear();
    }

    /// <summary>
    /// Writes this layer's composite (all elements, bottom to top) for region <paramref name="r"/> into a dense buffer.
    /// </summary>
    public void ReadComposite(PixelRect r, uint[] dest, int destOffset, int destStride, TextRenderCache textCache)
    {
        ArgumentNullException.ThrowIfNull(dest);
        ArgumentNullException.ThrowIfNull(textCache);
        var seg0 = (RasterSegment)Elements[0];
        if (Elements.Count == 1)
        {
            seg0.Pixels.ReadRect(r, dest, destOffset, destStride);
            return;
        }

        var w = seg0.Pixels.Width;
        var h = seg0.Pixels.Height;
        if (_cache is null || _cache.Width != w || _cache.Height != h)
        {
            _cache = new TiledSurface(w, h);
            _dirty.Clear();
            _dirty.Add(new PixelRect(0, 0, w, h));
        }

        for (var i = _dirty.Count - 1; i >= 0; i--)
        {
            var d = _dirty[i].Intersect(_cache.Bounds);
            if (d.IntersectsWith(r) || d.IsEmpty)
            {
                _dirty.RemoveAt(i);
                if (!d.IsEmpty)
                {
                    var tmp = new uint[d.Width * d.Height];
                    ComposeElements(d, tmp, 0, d.Width, textCache, Elements.Count);
                    _cache.WriteRect(d, tmp, 0, d.Width);
                }
            }
        }

        _cache.ReadRect(r, dest, destOffset, destStride);
    }

    /// <summary>
    /// Composes the first <paramref name="count"/> elements of the stack for region <paramref name="r"/> without
    /// using the cache.
    /// </summary>
    public void ComposeElements(PixelRect r, uint[] dest, int destOffset, int destStride, TextRenderCache textCache, int count)
    {
        ArgumentNullException.ThrowIfNull(textCache);
        var seg0 = (RasterSegment)Elements[0];
        var w = seg0.Pixels.Width;
        var h = seg0.Pixels.Height;
        seg0.Pixels.ReadRect(r, dest, destOffset, destStride);
        for (var i = 1; i < count; i++)
        {
            switch (Elements[i])
            {
                case TextObject t:
                    var rendered = textCache.Get(t, w, h);
                    if (rendered is not null && rendered.Bounds.IntersectsWith(r))
                    {
                        SurfaceOps.OverInto(rendered.Pixels, rendered.X, rendered.Y, r, dest, destOffset, destStride);
                    }

                    break;
                case RasterSegment s:
                    if (s.Erase is not null)
                    {
                        SurfaceOps.EraseInto(s.Erase, r, dest, destOffset, destStride);
                    }

                    SurfaceOps.OverInto(s.Pixels, r, dest, destOffset, destStride);
                    break;
            }
        }
    }

    /// <summary>Deep copy with fresh ids for the layer and every element (independent text objects).</summary>
    public Layer DuplicateIndependent(string name)
    {
        var els = Elements.Select<LayerElement, LayerElement>(e => e switch
        {
            TextObject t => t.CloneWithNewId(),
            RasterSegment s => s.CloneWithNewId(),
            _ => throw new InvalidOperationException("Unknown element."),
        });
        return new Layer(name, els)
        {
            Visible = Visible,
            Opacity = Opacity,
            BlendMode = BlendMode,
            IsBackground = false,
            IsTransparent = true,
        };
    }

    /// <summary>Copy sharing pixel tiles copy-on-write and keeping all ids (for history snapshots).</summary>
    public Layer CloneState() => new(Name, Elements.Select(e => e.CloneElement()))
    {
        Id = Id,
        Visible = Visible,
        Opacity = Opacity,
        BlendMode = BlendMode,
        IsBackground = IsBackground,
        IsTransparent = IsTransparent,
    };

    /// <summary>
    /// Removes empty segments that sit between two other segments-or-ends and merges adjacent segments so the
    /// stack does not grow forever after text objects are deleted.
    /// </summary>
    public void NormalizeStack()
    {
        for (var i = Elements.Count - 1; i > 0; i--)
        {
            if (Elements[i] is RasterSegment upper && Elements[i - 1] is RasterSegment lower)
            {
                if (upper.IsEmpty)
                {
                    Elements.RemoveAt(i);
                    continue;
                }

                MergeSegmentInto(lower, upper);
                Elements.RemoveAt(i);
            }
        }
    }

    /// <summary>Merges <paramref name="upper"/> onto <paramref name="lower"/> (lower is modified).</summary>
    private static void MergeSegmentInto(RasterSegment lower, RasterSegment upper)
    {
        var w = lower.Pixels.Width;
        var h = lower.Pixels.Height;
        var bounds = new PixelRect(0, 0, w, h);
        if (upper.Erase is not null && !upper.Erase.IsEmpty)
        {
            // Erasure of upper applies to lower's pixels and accumulates into lower's own erase mask.
            var lp = lower.Pixels.ToPixelBuffer();
            SurfaceOps.EraseInto(upper.Erase, bounds, lp.Pixels, 0, w);
            lower.Pixels = TiledSurface.FromPixelBuffer(lp);
            var le = lower.EnsureErase().ToPixelBuffer();
            var ue = upper.Erase.ToPixelBuffer();
            for (var i = 0; i < le.Pixels.Length; i++)
            {
                var a = (int)(le.Pixels[i] & 0xFF);
                var b = (int)(ue.Pixels[i] & 0xFF);
                le.Pixels[i] = (uint)(255 - ColorUtil.Mul(255 - a, 255 - b));
            }

            lower.Erase = TiledSurface.FromPixelBuffer(le);
        }

        var buf = new uint[w * h];
        lower.Pixels.ReadRect(bounds, buf, 0, w);
        SurfaceOps.OverInto(upper.Pixels, bounds, buf, 0, w);
        lower.Pixels = TiledSurface.FromPixelBuffer(new PixelBuffer(w, h, buf));
    }
}
