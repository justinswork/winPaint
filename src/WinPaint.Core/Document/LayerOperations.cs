using WinPaint.Core.Imaging;

namespace WinPaint.Core.Document;

/// <summary>Layer list operations. Each method commits exactly one undo step.</summary>
public static class LayerOperations
{
    /// <summary>Adds a transparent layer above the active one and makes it active.</summary>
    public static Layer Add(PaintDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var layer = new Layer(doc.Width, doc.Height, name);
        var index = doc.ActiveLayerIndex + 1;
        doc.Layers.Insert(index, layer);
        doc.ActiveLayerIndex = index;
        doc.InvalidateAll();
        doc.Commit("Add layer");
        return layer;
    }

    /// <summary>Deletes a layer (and its text objects). The last remaining layer can't be deleted.</summary>
    public static bool Delete(PaintDocument doc, int index)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Layers.Count <= 1 || index < 0 || index >= doc.Layers.Count)
        {
            return false;
        }

        doc.Layers.RemoveAt(index);
        doc.ActiveLayerIndex = Math.Clamp(index - 1, 0, doc.Layers.Count - 1);
        doc.InvalidateAll();
        doc.Commit("Delete layer");
        return true;
    }

    /// <summary>Duplicates a layer above itself; text objects become independent copies.</summary>
    public static Layer Duplicate(PaintDocument doc, int index, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var copy = doc.Layers[index].DuplicateIndependent(name);
        doc.Layers.Insert(index + 1, copy);
        doc.ActiveLayerIndex = index + 1;
        doc.InvalidateAll();
        doc.Commit("Duplicate layer");
        return copy;
    }

    /// <summary>Moves a layer to a new index.</summary>
    public static void Move(PaintDocument doc, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (from == to || from < 0 || to < 0 || from >= doc.Layers.Count || to >= doc.Layers.Count)
        {
            return;
        }

        var layer = doc.Layers[from];
        doc.Layers.RemoveAt(from);
        doc.Layers.Insert(to, layer);
        doc.ActiveLayerIndex = to;
        doc.InvalidateAll();
        doc.Commit("Move layer");
    }

    /// <summary>
    /// Merges a layer into the one below. With Normal blend and full opacity the upper element stack is placed on
    /// top of the lower one, so text stays editable. Otherwise the upper layer's text is flattened and its pixels are
    /// blended into a new top segment of the lower layer. A hidden upper layer contributes nothing.
    /// </summary>
    public static bool MergeDown(PaintDocument doc, int index)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (index <= 0 || index >= doc.Layers.Count)
        {
            return false;
        }

        var upper = doc.Layers[index];
        var lower = doc.Layers[index - 1];
        if (upper.Visible)
        {
            if (upper.BlendMode == BlendMode.Normal && upper.Opacity >= 1)
            {
                var appended = upper.Elements.ToList();
                lower.Elements.AddRange(appended);
                MergeSegmentsAt(lower, lower.Elements.Count - appended.Count);
            }
            else
            {
                MergeBlended(doc, upper, lower);
            }
        }

        doc.Layers.RemoveAt(index);
        doc.ActiveLayerIndex = index - 1;
        lower.InvalidateAll();
        doc.InvalidateAll();
        doc.Commit("Merge down");
        return true;
    }

    /// <summary>Merges all visible layers (and all live text) into a single background layer.</summary>
    public static void FlattenImage(PaintDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var flat = doc.Flatten();
        var bg = doc.Layers.FirstOrDefault(l => l.IsBackground);
        var layer = new Layer(doc.Width, doc.Height, bg?.Name ?? "Background")
        {
            IsBackground = true,
            IsTransparent = bg?.IsTransparent ?? flat.HasTransparency(),
        };
        layer.TopSegment.Pixels = TiledSurface.FromPixelBuffer(flat);
        doc.Layers.Clear();
        doc.Layers.Add(layer);
        doc.ActiveLayerIndex = 0;
        doc.InvalidateAll();
        doc.Commit("Flatten image");
    }

    private static void MergeSegmentsAt(Layer layer, int index)
    {
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

    private static void MergeBlended(PaintDocument doc, Layer upper, Layer lower)
    {
        var w = doc.Width;
        var h = doc.Height;
        var u = doc.LayerComposite(upper);
        var l = doc.LayerComposite(lower);
        var opacity = (int)Math.Round(Math.Clamp(upper.Opacity, 0, 1) * 255);
        var seg = new PixelBuffer(w, h);
        for (var i = 0; i < seg.Pixels.Length; i++)
        {
            var src = ColorUtil.Scale(u.Pixels[i], opacity);
            if (src == 0)
            {
                continue;
            }

            if (upper.BlendMode == BlendMode.Normal)
            {
                seg.Pixels[i] = src;
                continue;
            }

            // S over L == blend(src, L): S = cs(1−ab) + as·ab·B(Cs, Cb), alpha = as.
            var dst = l.Pixels[i];
            var blended = Blend.Pixel(upper.BlendMode, src, dst, 255);
            var inv = 255 - (int)ColorUtil.A(src);
            uint Channel(int shift)
            {
                var co = (int)((blended >> shift) & 0xFF);
                var cb = (int)((dst >> shift) & 0xFF);
                return (uint)Math.Clamp(co - ColorUtil.Mul(cb, inv), 0, 255);
            }

            seg.Pixels[i] = ColorUtil.Pack(ColorUtil.A(src), (byte)Channel(16), (byte)Channel(8), (byte)Channel(0));
        }

        lower.Elements.Add(new RasterSegment(TiledSurface.FromPixelBuffer(seg), null));
        MergeSegmentsAt(lower, lower.Elements.Count - 1);
    }
}
