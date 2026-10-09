using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Text;

/// <summary>A text object rendered to premultiplied pixels, positioned in canvas space.</summary>
/// <param name="Pixels">Rendered pixels.</param>
/// <param name="X">Canvas x of the buffer's left edge.</param>
/// <param name="Y">Canvas y of the buffer's top edge.</param>
public sealed record RenderedText(PixelBuffer Pixels, int X, int Y)
{
    /// <summary>Canvas-space bounds of the buffer.</summary>
    public PixelRect Bounds => new(X, Y, Pixels.Width, Pixels.Height);
}

/// <summary>Renders text objects to pixels (vector re-render, applying the object's transform).</summary>
public static class TextRenderer
{
    /// <summary>
    /// Renders the text object clipped to the canvas. Returns null when nothing is visible.
    /// Must be called on an STA thread.
    /// </summary>
    public static RenderedText? Render(TextObject t, int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(t);
        if (string.IsNullOrEmpty(t.Text) && !t.OpaqueBackground)
        {
            return null;
        }

        var box = TextLayoutEngine.EffectiveBox(t);
        var bounds = TextLayoutEngine.TransformedBounds(t);

        // Text decorations and glyph overhangs (italics) can extend slightly past the layout box.
        var pad = Math.Ceiling(t.FontSizePx * 0.5) + 2;
        var scale = Math.Sqrt(Math.Abs(t.Transform.Determinant));
        bounds.Inflate(pad * Math.Max(1, scale), pad * Math.Max(1, scale));
        var area = PixelRect.FromRect(bounds).Intersect(new PixelRect(0, 0, canvasWidth, canvasHeight));
        if (area.IsEmpty)
        {
            return null;
        }

        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-area.X, -area.Y));
            dc.PushTransform(new MatrixTransform(t.Transform));
            dc.PushClip(new RectangleGeometry(box));
            dc.PushOpacity(Math.Clamp(t.Opacity, 0, 1));
            if (t.OpaqueBackground)
            {
                dc.DrawRectangle(TextLayoutEngine.CreateFrozenBrush(t.Background), null, box);
            }

            if (!string.IsNullOrEmpty(t.Text))
            {
                dc.DrawText(TextLayoutEngine.Build(t), box.TopLeft);
            }

            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var buf = new PixelBuffer(area.Width, area.Height);
        rtb.CopyPixels(buf.Pixels, area.Width * 4, 0);
        return new RenderedText(buf, area.X, area.Y);
    }
}

/// <summary>Caches rendered text by object id and render key.</summary>
public sealed class TextRenderCache
{
    private readonly Dictionary<Guid, (string Key, int W, int H, RenderedText? Render)> _cache = [];

    /// <summary>Returns the (possibly cached) rendering of a text object.</summary>
    public RenderedText? Get(TextObject t, int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(t);
        var key = t.RenderKey();
        if (_cache.TryGetValue(t.Id, out var e) && e.Key == key && e.W == canvasWidth && e.H == canvasHeight)
        {
            return e.Render;
        }

        var r = TextRenderer.Render(t, canvasWidth, canvasHeight);
        _cache[t.Id] = (key, canvasWidth, canvasHeight, r);
        return r;
    }

    /// <summary>Canvas bounds of the cached render (empty when none).</summary>
    public PixelRect CachedBounds(Guid id) =>
        _cache.TryGetValue(id, out var e) && e.Render is not null ? e.Render.Bounds : PixelRect.Empty;

    /// <summary>Drops cache entries for objects no longer alive.</summary>
    public void Prune(IEnumerable<Guid> alive)
    {
        var set = alive.ToHashSet();
        foreach (var id in _cache.Keys.Where(k => !set.Contains(k)).ToList())
        {
            _cache.Remove(id);
        }
    }

    /// <summary>Clears all entries.</summary>
    public void Clear() => _cache.Clear();
}
