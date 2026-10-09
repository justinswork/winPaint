using WinPaint.Core.Imaging;
using WinPaint.Core.Text;

namespace WinPaint.Core.Document;

/// <summary>
/// Structural operations on live text objects. These keep the element-stack invariant: a text object keeps its
/// position, so pixels painted after it stay above it and pixels painted before stay beneath.
/// None of these commit history; callers do.
/// </summary>
public static class TextOperations
{
    /// <summary>
    /// Pushes a text object on top of the layer's stack followed by a new empty raster segment, so everything
    /// painted afterwards lands above the text.
    /// </summary>
    public static void Insert(PaintDocument doc, Layer layer, TextObject text)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(text);
        layer.Elements.Add(text);
        layer.Elements.Add(new RasterSegment(doc.Width, doc.Height));
        doc.Invalidate(layer, RenderBounds(doc, text));
    }

    /// <summary>Applies a change to a live text object and repaints old ∪ new bounds.</summary>
    public static void Update(PaintDocument doc, Layer layer, TextObject text, Action<TextObject> change)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(change);
        var old = RenderBounds(doc, text);
        change(text);
        doc.Invalidate(layer, old.Union(RenderBounds(doc, text)));
    }

    /// <summary>Removes a text object, revealing what was beneath it, and merges the now-adjacent segments.</summary>
    public static void Remove(PaintDocument doc, Layer layer, TextObject text)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(text);
        var bounds = RenderBounds(doc, text);
        var index = layer.Elements.IndexOf(text);
        if (index < 0)
        {
            return;
        }

        layer.Elements.RemoveAt(index);
        MergeAround(layer, index);
        layer.InvalidateAll();
        doc.Invalidate(layer, bounds);
    }

    /// <summary>
    /// Converts a text object into pixels at its own stack position (rendered into the segment directly below it),
    /// then removes it as an object. The layer composite is unchanged.
    /// </summary>
    public static void Flatten(PaintDocument doc, Layer layer, TextObject text)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(text);
        var index = layer.Elements.IndexOf(text);
        if (index <= 0)
        {
            return;
        }

        var rendered = doc.TextCache.Get(text, doc.Width, doc.Height);
        var below = (RasterSegment)layer.Elements[index - 1];
        if (rendered is not null)
        {
            var r = rendered.Bounds;
            var buf = new uint[r.Width * r.Height];
            below.Pixels.ReadRect(r, buf, 0, r.Width);
            SurfaceOps.OverInto(rendered.Pixels, rendered.X, rendered.Y, r, buf, 0, r.Width);
            below.Pixels.WriteRect(r, buf, 0, r.Width);
        }

        layer.Elements.RemoveAt(index);
        MergeAround(layer, index);
        layer.InvalidateAll();
        doc.Invalidate(layer, rendered?.Bounds ?? PixelRect.Empty);
    }

    /// <summary>
    /// Flattens every live text object of <paramref name="layer"/> whose rendered bounds intersect
    /// <paramref name="area"/>. Returns true when anything was flattened.
    /// </summary>
    public static bool FlattenIntersecting(PaintDocument doc, Layer layer, PixelRect area)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        var hits = layer.TextObjects.Where(t => RenderBounds(doc, t).IntersectsWith(area)).ToList();
        foreach (var t in hits)
        {
            Flatten(doc, layer, t);
        }

        return hits.Count > 0;
    }

    /// <summary>Flattens every text object on a layer.</summary>
    public static bool FlattenAll(PaintDocument doc, Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var any = false;
        foreach (var t in layer.TextObjects.ToList())
        {
            Flatten(doc, layer, t);
            any = true;
        }

        return any;
    }

    /// <summary>Canvas bounds covered by the text's rendering (box ∪ glyph overhang), clipped to the canvas.</summary>
    public static PixelRect RenderBounds(PaintDocument doc, TextObject text)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(text);
        var box = PixelRect.FromRect(TextLayoutEngine.TransformedBounds(text)).Inflate((int)Math.Ceiling(text.FontSizePx) + 4);
        return box.Union(doc.TextCache.CachedBounds(text.Id)).Intersect(doc.Bounds);
    }

    private static void MergeAround(Layer layer, int index)
    {
        // After removal, Elements[index - 1] and Elements[index] are both segments; merge them.
        if (index <= 0 || index >= layer.Elements.Count)
        {
            return;
        }

        if (layer.Elements[index - 1] is RasterSegment && layer.Elements[index] is RasterSegment)
        {
            var tmp = new Layer("tmp", [layer.Elements[index - 1], layer.Elements[index]]);
            tmp.NormalizeStack();
            layer.Elements[index - 1] = tmp.Elements[0];
            layer.Elements.RemoveAt(index);
        }
    }
}
