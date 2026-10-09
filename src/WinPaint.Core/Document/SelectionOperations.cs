using System.Windows.Media;
using WinPaint.Core.Imaging;
using WinPaint.Core.Tools;

namespace WinPaint.Core.Document;

/// <summary>Pixel operations used by selections (lift, clear, stamp).</summary>
public static class SelectionOperations
{
    /// <summary>
    /// Flattens live text on <paramref name="layer"/> intersecting <paramref name="area"/> (selection edits are pixel
    /// edits). Returns true when text was flattened.
    /// </summary>
    public static bool FlattenTextIn(PaintDocument doc, Layer layer, PixelRect area) =>
        TextOperations.FlattenIntersecting(doc, layer, area);

    /// <summary>Copies the layer's composite pixels inside the region (alpha 0 outside the mask).</summary>
    public static PixelBuffer Extract(PaintDocument doc, Layer layer, SelectionRegion region)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(region);
        var r = region.Bounds;
        var buf = doc.LayerComposite(layer, r);
        if (region.Mask is { } mask)
        {
            for (var i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    buf.Pixels[i] = 0;
                }
            }
        }

        return buf;
    }

    /// <summary>
    /// Clears the selected pixels: the secondary color on an opaque background layer, transparency elsewhere
    /// (also erasing whatever lies below in the layer stack).
    /// </summary>
    public static void Clear(PaintDocument doc, Layer layer, SelectionRegion region, Color secondary)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(region);
        var r = region.Bounds;
        var seg = layer.TopSegment;
        var px = new uint[r.Width * r.Height];
        seg.Pixels.ReadRect(r, px, 0, r.Width);
        if (!layer.ErasesToTransparent)
        {
            // The background stays opaque: a translucent secondary color is composited onto white paper.
            var fill = ColorUtil.Over(ColorUtil.FromColor(secondary), ColorUtil.White);
            for (var i = 0; i < px.Length; i++)
            {
                if (region.Mask is null || region.Mask[i])
                {
                    px[i] = fill;
                }
            }

            seg.Pixels.WriteRect(r, px, 0, r.Width);
        }
        else
        {
            uint[]? erase = null;
            if (layer.Elements.Count > 1)
            {
                erase = new uint[px.Length];
                seg.Erase?.ReadRect(r, erase, 0, r.Width);
            }

            for (var i = 0; i < px.Length; i++)
            {
                if (region.Mask is null || region.Mask[i])
                {
                    px[i] = 0;
                    if (erase is not null)
                    {
                        erase[i] = 255;
                    }
                }
            }

            seg.Pixels.WriteRect(r, px, 0, r.Width);
            if (erase is not null)
            {
                seg.EnsureErase().WriteRect(r, erase, 0, r.Width);
            }
        }

        doc.Invalidate(layer, r);
    }

    /// <summary>Composites pixels over the layer's top segment at (x, y).</summary>
    public static void Stamp(PaintDocument doc, Layer layer, PixelBuffer pixels, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(pixels);
        var r = new PixelRect(x, y, pixels.Width, pixels.Height).Intersect(doc.Bounds);
        if (r.IsEmpty)
        {
            return;
        }

        var seg = layer.TopSegment.Pixels;
        var buf = new uint[r.Width * r.Height];
        seg.ReadRect(r, buf, 0, r.Width);
        SurfaceOps.OverInto(pixels, x, y, r, buf, 0, r.Width);
        seg.WriteRect(r, buf, 0, r.Width);
        doc.Invalidate(layer, r);
    }

    /// <summary>Makes pixels equal to <paramref name="key"/> transparent (transparent selection).</summary>
    public static PixelBuffer KeyOut(PixelBuffer pixels, Color key)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var k = ColorUtil.FromColor(key);
        var result = pixels.Clone();
        for (var i = 0; i < result.Pixels.Length; i++)
        {
            if (result.Pixels[i] == k)
            {
                result.Pixels[i] = 0;
            }
        }

        return result;
    }
}
