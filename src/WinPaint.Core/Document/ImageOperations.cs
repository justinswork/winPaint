using System.Windows;
using System.Windows.Media;
using WinPaint.Core.Imaging;
using WinPaint.Core.Text;

namespace WinPaint.Core.Document;

/// <summary>
/// Whole-image operations. Raster segments are transformed as pixels and live text objects get the same transform
/// appended to their <see cref="TextObject.Transform"/>, so they stay editable and re-render crisply.
/// Each method commits exactly one undo step. The *Async variants run the pixel work through an
/// <see cref="Offload"/> (e.g. a background task) while the document is only touched on the calling thread.
/// </summary>
public static class ImageOperations
{
    /// <summary>Runs pure pixel work somewhere (inline or on a background thread).</summary>
    /// <param name="work">The work; it must not touch the document or any UI object.</param>
    /// <returns>A task completing when the work has run.</returns>
    public delegate Task Offload(Action work);

    /// <summary>Runs the work synchronously on the calling thread.</summary>
    public static Offload Inline { get; } = work =>
    {
        work();
        return Task.CompletedTask;
    };

    /// <summary>Rotates or flips the whole image.</summary>
    public static void Orthogonal(PaintDocument doc, OrthoTransform t) => OrthogonalAsync(doc, t, Inline).GetAwaiter().GetResult();

    /// <summary>Rotates or flips the whole image, offloading the pixel work.</summary>
    public static async Task OrthogonalAsync(PaintDocument doc, OrthoTransform t, Offload offload)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var (w, h) = (doc.Width, doc.Height);
        var (nw, nh) = Transforms.SizeAfter(t, w, h);
        var m = Transforms.MatrixFor(t, w, h);
        await TransformAllAsync(doc, nw, nh, (buf, _) => Transforms.Apply(buf, t), m, offload).ConfigureAwait(true);
        doc.Commit(t switch
        {
            OrthoTransform.FlipHorizontal => "Flip horizontal",
            OrthoTransform.FlipVertical => "Flip vertical",
            _ => "Rotate",
        });
    }

    /// <summary>Resizes the whole image (high quality; nearest neighbor for exact integer upscales).</summary>
    public static void Resize(PaintDocument doc, int width, int height) => ResizeAsync(doc, width, height, Inline).GetAwaiter().GetResult();

    /// <summary>Resizes the whole image, offloading the pixel work.</summary>
    public static async Task ResizeAsync(PaintDocument doc, int width, int height, Offload offload)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (width == doc.Width && height == doc.Height)
        {
            return;
        }

        var m = new Matrix((double)width / doc.Width, 0, 0, (double)height / doc.Height, 0, 0);
        await TransformAllAsync(doc, width, height, (buf, _) => Resampler.ResizeAuto(buf, width, height), m, offload).ConfigureAwait(true);
        doc.Commit("Resize");
    }

    /// <summary>Skews the whole image by horizontal/vertical angles in degrees.</summary>
    public static void Skew(PaintDocument doc, double horizontalDeg, double verticalDeg, Color secondary) =>
        SkewAsync(doc, horizontalDeg, verticalDeg, secondary, Inline).GetAwaiter().GetResult();

    /// <summary>Skews the whole image, offloading the pixel work.</summary>
    public static async Task SkewAsync(PaintDocument doc, double horizontalDeg, double verticalDeg, Color secondary, Offload offload)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (horizontalDeg == 0 && verticalDeg == 0)
        {
            return;
        }

        var (m, nw, nh) = Transforms.SkewGeometry(doc.Width, doc.Height, horizontalDeg, verticalDeg);
        var fill = ColorUtil.FromColor(secondary);
        await TransformAllAsync(doc, nw, nh, (buf, opaqueBase) => Transforms.Affine(buf, m, nw, nh, opaqueBase ? fill : 0), m, offload).ConfigureAwait(true);
        doc.Commit("Skew");
    }

    /// <summary>
    /// Crops to a rectangle. Text entirely outside is removed (undo restores it); partly-outside text is kept.
    /// </summary>
    public static void Crop(PaintDocument doc, PixelRect rect, bool commit = true)
    {
        ArgumentNullException.ThrowIfNull(doc);
        rect = rect.Intersect(doc.Bounds);
        if (rect.IsEmpty)
        {
            return;
        }

        var keep = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        foreach (var layer in doc.Layers)
        {
            foreach (var t in layer.TextObjects.ToList())
            {
                var b = TextLayoutEngine.TransformedBounds(t);
                if (!b.IntersectsWith(keep) || b.Width == 0)
                {
                    TextOperations.Remove(doc, layer, t);
                }
            }
        }

        var m = new Matrix(1, 0, 0, 1, -rect.X, -rect.Y);
        TransformAllAsync(doc, rect.Width, rect.Height, (buf, _) => buf.Crop(rect), m, Inline).GetAwaiter().GetResult();
        if (commit)
        {
            doc.Commit("Crop");
        }
    }

    /// <summary>
    /// Changes the canvas size anchored at the top-left without scaling. New area: the secondary color on an opaque
    /// background layer, transparent elsewhere. Text objects keep their positions (clipped when outside).
    /// </summary>
    public static void ResizeCanvas(PaintDocument doc, int width, int height, Color secondary)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (width == doc.Width && height == doc.Height)
        {
            return;
        }

        var oldW = doc.Width;
        var oldH = doc.Height;
        var fill = ColorUtil.FromColor(secondary);
        foreach (var layer in doc.Layers)
        {
            for (var i = 0; i < layer.Elements.Count; i++)
            {
                if (layer.Elements[i] is not RasterSegment s)
                {
                    continue;
                }

                var opaqueBase = i == 0 && layer.IsBackground && !layer.IsTransparent;
                s.Pixels = Recanvas(s.Pixels, width, height, opaqueBase ? fill : 0, oldW, oldH);
                if (s.Erase is not null)
                {
                    s.Erase = Recanvas(s.Erase, width, height, 0, oldW, oldH);
                }
            }
        }

        doc.SetSize(width, height);
        doc.InvalidateAll();
        doc.Commit("Resize canvas");
    }

    /// <summary>Inverts the colors of the active layer, including its live text colors.</summary>
    public static void InvertColors(PaintDocument doc) => InvertColorsAsync(doc, Inline).GetAwaiter().GetResult();

    /// <summary>Inverts the colors of the active layer, offloading the pixel work.</summary>
    public static async Task InvertColorsAsync(PaintDocument doc, Offload offload)
    {
        ArgumentNullException.ThrowIfNull(doc);
        await MapSegmentsAsync(
            [doc.ActiveLayer],
            b => Transforms.Invert(b),
            t =>
            {
                t.Foreground = ColorUtil.Invert(t.Foreground);
                t.Background = ColorUtil.Invert(t.Background);
            },
            offload).ConfigureAwait(true);
        doc.InvalidateAll();
        doc.Commit("Invert colors");
    }

    /// <summary>Converts every layer to black and white (luminance threshold), including text colors.</summary>
    public static void BlackAndWhite(PaintDocument doc) => BlackAndWhiteAsync(doc, Inline).GetAwaiter().GetResult();

    /// <summary>Converts every layer to black and white, offloading the pixel work.</summary>
    public static async Task BlackAndWhiteAsync(PaintDocument doc, Offload offload)
    {
        ArgumentNullException.ThrowIfNull(doc);
        await MapSegmentsAsync(
            doc.Layers,
            b =>
            {
                for (var i = 0; i < b.Pixels.Length; i++)
                {
                    b.Pixels[i] = ColorUtil.ToBlackWhite(b.Pixels[i]);
                }
            },
            t =>
            {
                t.Foreground = ColorUtil.ToBlackWhite(t.Foreground);
                t.Background = ColorUtil.ToBlackWhite(t.Background);
            },
            offload).ConfigureAwait(true);
        doc.InvalidateAll();
        doc.Commit("Black and white");
    }

    /// <summary>Makes the background layer transparent or opaque (F-IMG-05).</summary>
    public static void SetTransparentCanvas(PaintDocument doc, bool transparent)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var bg = doc.Layers.FirstOrDefault(l => l.IsBackground);
        if (bg is null || bg.IsTransparent == transparent)
        {
            return;
        }

        bg.IsTransparent = transparent;
        var seg0 = (RasterSegment)bg.Elements[0];
        if (transparent)
        {
            // Remove the white paper so the checkerboard shows through.
            var b = seg0.Pixels.ToPixelBuffer();
            for (var i = 0; i < b.Pixels.Length; i++)
            {
                if (b.Pixels[i] == ColorUtil.White)
                {
                    b.Pixels[i] = 0;
                }
            }

            seg0.Pixels = TiledSurface.FromPixelBuffer(b);
        }
        else
        {
            var b = seg0.Pixels.ToPixelBuffer();
            for (var i = 0; i < b.Pixels.Length; i++)
            {
                b.Pixels[i] = ColorUtil.Over(b.Pixels[i], ColorUtil.White);
            }

            seg0.Pixels = TiledSurface.FromPixelBuffer(b);
        }

        doc.InvalidateAll();
        doc.Commit(transparent ? "Transparent canvas" : "Opaque canvas");
    }

    private static TiledSurface Recanvas(TiledSurface s, int w, int h, uint fill, int oldW, int oldH)
    {
        var n = fill == 0 ? new TiledSurface(w, h) : TiledSurface.CreateFilled(w, h, fill);
        var copy = new PixelRect(0, 0, Math.Min(oldW, w), Math.Min(oldH, h));
        var buf = new uint[copy.Width * copy.Height];
        s.ReadRect(copy, buf, 0, copy.Width);
        n.WriteRect(copy, buf, 0, copy.Width);
        n.Compact();
        return n;
    }

    private static async Task MapSegmentsAsync(IEnumerable<Layer> layers, Action<PixelBuffer> pixelOp, Action<TextObject> textOp, Offload offload)
    {
        var jobs = new List<(RasterSegment Segment, PixelBuffer Source)>();
        foreach (var layer in layers)
        {
            foreach (var e in layer.Elements)
            {
                switch (e)
                {
                    case RasterSegment s:
                        jobs.Add((s, s.Pixels.ToPixelBuffer()));
                        break;
                    case TextObject t:
                        textOp(t);
                        break;
                }
            }
        }

        var results = new TiledSurface[jobs.Count];
        await offload(() =>
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                pixelOp(jobs[i].Source);
                results[i] = TiledSurface.FromPixelBuffer(jobs[i].Source);
            }
        }).ConfigureAwait(true);
        for (var i = 0; i < jobs.Count; i++)
        {
            jobs[i].Segment.Pixels = results[i];
        }
    }

    private static async Task TransformAllAsync(PaintDocument doc, int nw, int nh, Func<PixelBuffer, bool, PixelBuffer> pixelOp, Matrix textMatrix, Offload offload)
    {
        // Gather on the calling thread, transform pure pixel buffers through the offload, then apply.
        var jobs = new List<(RasterSegment Segment, bool Erase, PixelBuffer Source, bool OpaqueBase)>();
        foreach (var layer in doc.Layers)
        {
            for (var i = 0; i < layer.Elements.Count; i++)
            {
                if (layer.Elements[i] is RasterSegment s)
                {
                    var opaqueBase = i == 0 && layer.IsBackground && !layer.IsTransparent;
                    jobs.Add((s, false, s.Pixels.ToPixelBuffer(), opaqueBase));
                    if (s.Erase is not null)
                    {
                        jobs.Add((s, true, s.Erase.ToPixelBuffer(), false));
                    }
                }
            }
        }

        var results = new TiledSurface[jobs.Count];
        await offload(() =>
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                results[i] = TiledSurface.FromPixelBuffer(pixelOp(jobs[i].Source, jobs[i].OpaqueBase));
            }
        }).ConfigureAwait(true);

        for (var i = 0; i < jobs.Count; i++)
        {
            if (jobs[i].Erase)
            {
                jobs[i].Segment.Erase = results[i];
            }
            else
            {
                jobs[i].Segment.Pixels = results[i];
            }
        }

        foreach (var t in doc.AllText.Select(h => h.Text))
        {
            var m = t.Transform;
            m.Append(textMatrix);
            t.Transform = m;
        }

        if (doc.Floating is not null)
        {
            doc.Floating = null;
        }

        doc.SetSize(nw, nh);
        doc.InvalidateAll();
    }
}