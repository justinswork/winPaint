using System.Windows;
using WinPaint.Core.History;
using WinPaint.Core.Imaging;
using WinPaint.Core.Projects;
using WinPaint.Core.Text;

namespace WinPaint.Core.Document;

/// <summary>Pixels floating above the active layer (a lifted selection or an uncommitted shape).</summary>
/// <param name="Pixels">Premultiplied pixels.</param>
/// <param name="X">Canvas x of the left edge.</param>
/// <param name="Y">Canvas y of the top edge.</param>
public sealed record FloatingImage(PixelBuffer Pixels, int X, int Y)
{
    /// <summary>Canvas bounds.</summary>
    public PixelRect Bounds => new(X, Y, Pixels.Width, Pixels.Height);
}

/// <summary>Result of a text hit test.</summary>
/// <param name="Layer">Layer holding the text.</param>
/// <param name="Text">The text object.</param>
public sealed record TextHit(Layer Layer, TextObject Text);

/// <summary>
/// The in-memory image: canvas size, layers (bottom to top), active layer, floating content and history.
/// </summary>
public sealed class PaintDocument
{
    private FloatingImage? _floating;

    /// <summary>Creates a document with one background layer filled with <paramref name="background"/>.</summary>
    public PaintDocument(int width, int height, uint background = ColorUtil.White)
    {
        Width = width;
        Height = height;
        var layer = new Layer(width, height, "Background")
        {
            IsBackground = true,
            IsTransparent = ColorUtil.A(background) < 255,
        };
        if (background != 0)
        {
            layer.TopSegment.Pixels = TiledSurface.CreateFilled(width, height, background);
        }

        Layers.Add(layer);
        History.Reset(CaptureState(), markSaved: true);
    }

    /// <summary>Raised when pixels in a region changed (canvas coordinates).</summary>
    public event EventHandler<PixelRect>? Invalidated;

    /// <summary>Raised when layers, size or structure changed.</summary>
    public event EventHandler? StructureChanged;

    /// <summary>Canvas width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Canvas height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Horizontal resolution (DPI).</summary>
    public double DpiX { get; set; } = 96;

    /// <summary>Vertical resolution (DPI).</summary>
    public double DpiY { get; set; } = 96;

    /// <summary>Canvas bounds.</summary>
    public PixelRect Bounds => new(0, 0, Width, Height);

    /// <summary>Layers, bottom to top.</summary>
    public List<Layer> Layers { get; } = [];

    /// <summary>Index of the active layer.</summary>
    public int ActiveLayerIndex { get; set; }

    /// <summary>The layer tools paint on.</summary>
    public Layer ActiveLayer => Layers[Math.Clamp(ActiveLayerIndex, 0, Layers.Count - 1)];

    /// <summary>Extension (plugin) data saved with the project. Change it, then <see cref="Commit"/> for undo.</summary>
    public ProjectExtensions Extensions { get; set; } = ProjectExtensions.Empty;

    /// <summary>Rendered text cache shared by every layer.</summary>
    public TextRenderCache TextCache { get; } = new();

    /// <summary>Undo/redo history.</summary>
    public HistoryManager History { get; } = new();

    /// <summary>Unsaved changes.</summary>
    public bool IsDirty => History.IsDirty;

    /// <summary>Content floating above the active layer (not part of history until committed).</summary>
    public FloatingImage? Floating
    {
        get => _floating;
        set
        {
            var old = _floating?.Bounds ?? PixelRect.Empty;
            _floating = value;
            RaiseInvalidated(old.Union(value?.Bounds ?? PixelRect.Empty));
        }
    }

    /// <summary>Every live text object with its layer, bottom-most layer first.</summary>
    public IEnumerable<TextHit> AllText => Layers.SelectMany(l => l.TextObjects.Select(t => new TextHit(l, t)));

    /// <summary>Creates a document from an image (one plain raster layer).</summary>
    public static PaintDocument FromImage(PixelBuffer image, double dpiX = 96, double dpiY = 96)
    {
        ArgumentNullException.ThrowIfNull(image);
        var doc = new PaintDocument(image.Width, image.Height, 0) { DpiX = dpiX, DpiY = dpiY };
        var layer = doc.Layers[0];
        layer.TopSegment.Pixels = TiledSurface.FromPixelBuffer(image);
        layer.IsTransparent = image.HasTransparency();
        doc.History.Reset(doc.CaptureState(), markSaved: true);
        return doc;
    }

    /// <summary>Creates a document from a snapshot (e.g. an opened project); it opens clean with empty history.</summary>
    public static PaintDocument FromState(DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var doc = new PaintDocument(1, 1, 0);
        doc.RestoreState(state);
        doc.History.Reset(doc.CaptureState(), markSaved: true);
        return doc;
    }

    /// <summary>Marks a region of a layer changed and notifies listeners.</summary>
    public void Invalidate(Layer layer, PixelRect r)
    {
        ArgumentNullException.ThrowIfNull(layer);
        layer.Invalidate(r.Intersect(Bounds));
        RaiseInvalidated(r);
    }

    /// <summary>Discards every cache and notifies a full repaint plus structure change.</summary>
    public void InvalidateAll()
    {
        foreach (var l in Layers)
        {
            l.InvalidateAll();
        }

        TextCache.Prune(AllText.Select(h => h.Text.Id));
        StructureChanged?.Invoke(this, EventArgs.Empty);
        RaiseInvalidated(Bounds);
    }

    /// <summary>Notifies that layer list/properties changed (no pixel change implied).</summary>
    public void RaiseStructureChanged() => StructureChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Composites the visible document (including floating content) for region <paramref name="r"/>.</summary>
    public void RenderComposite(PixelRect r, uint[] dest, int destOffset, int destStride, bool includeFloating = true)
    {
        ArgumentNullException.ThrowIfNull(dest);
        var floating = includeFloating ? _floating : null;
        var visible = Layers.Where(l => l.Visible).ToList();
        if (visible.Count == 1 && visible[0].BlendMode == BlendMode.Normal && visible[0].Opacity >= 1 && (floating is null || visible[0] != ActiveLayer))
        {
            visible[0].ReadComposite(r, dest, destOffset, destStride, TextCache);
            return;
        }

        for (var y = 0; y < r.Height; y++)
        {
            dest.AsSpan(destOffset + (y * destStride), r.Width).Clear();
        }

        var tmp = new uint[r.Width * r.Height];
        foreach (var layer in visible)
        {
            layer.ReadComposite(r, tmp, 0, r.Width, TextCache);
            if (floating is not null && layer == ActiveLayer)
            {
                SurfaceOps.OverInto(floating.Pixels, floating.X, floating.Y, r, tmp, 0, r.Width);
            }

            var opacity = (int)Math.Round(Math.Clamp(layer.Opacity, 0, 1) * 255);
            for (var y = 0; y < r.Height; y++)
            {
                Blend.Row(layer.BlendMode, tmp.AsSpan(y * r.Width, r.Width), dest.AsSpan(destOffset + (y * destStride), r.Width), opacity);
            }
        }
    }

    /// <summary>Returns the composite of the visible document (without floating content) as a new buffer.</summary>
    public PixelBuffer Flatten(bool includeFloating = false)
    {
        var buf = new PixelBuffer(Width, Height);
        RenderComposite(Bounds, buf.Pixels, 0, Width, includeFloating);
        return buf;
    }

    /// <summary>Composite of a region as a new buffer.</summary>
    public PixelBuffer RenderRegion(PixelRect r, bool includeFloating = true)
    {
        var buf = new PixelBuffer(r.Width, r.Height);
        RenderComposite(r, buf.Pixels, 0, r.Width, includeFloating);
        return buf;
    }

    /// <summary>One composited pixel (for the color picker).</summary>
    public uint CompositePixel(int x, int y)
    {
        if (!Bounds.Contains(x, y))
        {
            return 0;
        }

        var one = new uint[1];
        RenderComposite(new PixelRect(x, y, 1, 1), one, 0, 1);
        return one[0];
    }

    /// <summary>The composite of a single layer (all its elements) as a buffer.</summary>
    public PixelBuffer LayerComposite(Layer layer, PixelRect? region = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var r = region ?? Bounds;
        var buf = new PixelBuffer(r.Width, r.Height);
        layer.ReadComposite(r, buf.Pixels, 0, r.Width, TextCache);
        return buf;
    }

    /// <summary>Topmost live text object at a canvas point among visible layers.</summary>
    public TextHit? HitTestText(Point p)
    {
        for (var li = Layers.Count - 1; li >= 0; li--)
        {
            var layer = Layers[li];
            if (!layer.Visible)
            {
                continue;
            }

            for (var ei = layer.Elements.Count - 1; ei >= 0; ei--)
            {
                if (layer.Elements[ei] is TextObject t && TextLayoutEngine.HitTest(t, p))
                {
                    return new TextHit(layer, t);
                }
            }
        }

        return null;
    }

    /// <summary>Finds a text object by id.</summary>
    public TextHit? FindText(Guid id)
    {
        foreach (var l in Layers)
        {
            foreach (var t in l.TextObjects)
            {
                if (t.Id == id)
                {
                    return new TextHit(l, t);
                }
            }
        }

        return null;
    }

    /// <summary>Finds a layer by id.</summary>
    public Layer? FindLayer(Guid id) => Layers.FirstOrDefault(l => l.Id == id);

    /// <summary>Snapshot of the content (cheap; tiles are shared copy-on-write).</summary>
    public DocumentState CaptureState() =>
        new(Width, Height, DpiX, DpiY, Layers.Select(l => l.CloneState()).ToList(), ActiveLayerIndex, Extensions);

    /// <summary>Replaces the content with a snapshot.</summary>
    public void RestoreState(DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _floating = null;
        Width = state.Width;
        Height = state.Height;
        DpiX = state.DpiX;
        DpiY = state.DpiY;
        Layers.Clear();
        Layers.AddRange(state.Layers.Select(l => l.CloneState()));
        ActiveLayerIndex = Math.Clamp(state.ActiveLayerIndex, 0, Layers.Count - 1);
        Extensions = state.Extensions;
        InvalidateAll();
    }

    /// <summary>Records the current content as one undo step.</summary>
    public void Commit(string name) => History.Commit(name, CaptureState());

    /// <summary>Discards uncommitted changes, returning to the last committed state.</summary>
    public void RevertUncommitted() => RestoreState(History.Current);

    /// <summary>Undoes one step.</summary>
    public bool Undo()
    {
        if (!History.CanUndo)
        {
            return false;
        }

        RestoreState(History.Undo());
        return true;
    }

    /// <summary>Redoes one step.</summary>
    public bool Redo()
    {
        if (!History.CanRedo)
        {
            return false;
        }

        RestoreState(History.Redo());
        return true;
    }

    /// <summary>Changes the canvas size (used by whole-image operations; callers rebuild surfaces).</summary>
    public void SetSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    private void RaiseInvalidated(PixelRect r)
    {
        r = r.Intersect(Bounds);
        if (!r.IsEmpty)
        {
            Invalidated?.Invoke(this, r);
        }
    }
}
