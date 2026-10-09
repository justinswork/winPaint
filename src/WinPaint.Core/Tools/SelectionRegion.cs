using System.Windows;
using WinPaint.Core.Brushes;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Tools;

/// <summary>
/// A selected area: a bounding rectangle plus an optional pixel mask (free-form or inverted selections) and the
/// outlines used to draw marching ants.
/// </summary>
public sealed class SelectionRegion
{
    private SelectionRegion(PixelRect bounds, bool[]? mask, IReadOnlyList<IReadOnlyList<Point>> outlines, bool isFreeForm)
    {
        Bounds = bounds;
        Mask = mask;
        Outlines = outlines;
        IsFreeForm = isFreeForm;
    }

    /// <summary>Bounding rectangle (canvas px).</summary>
    public PixelRect Bounds { get; }

    /// <summary>Row-major mask over <see cref="Bounds"/>; null means the whole rectangle.</summary>
    public bool[]? Mask { get; }

    /// <summary>Outlines for marching ants (canvas coordinates).</summary>
    public IReadOnlyList<IReadOnlyList<Point>> Outlines { get; }

    /// <summary>True for free-form (or inverted) selections.</summary>
    public bool IsFreeForm { get; }

    /// <summary>A rectangular selection.</summary>
    public static SelectionRegion FromRect(PixelRect r) =>
        new(r, null, [[new(r.X, r.Y), new(r.Right, r.Y), new(r.Right, r.Bottom), new(r.X, r.Bottom)]], false);

    /// <summary>A free-form selection from a lasso polygon, clipped to the canvas.</summary>
    public static SelectionRegion? FromPolygon(IReadOnlyList<Point> polygon, PixelRect canvas)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3)
        {
            return null;
        }

        var bounds = PixelRect.FromEdges(
            (int)Math.Floor(polygon.Min(p => p.X)),
            (int)Math.Floor(polygon.Min(p => p.Y)),
            (int)Math.Ceiling(polygon.Max(p => p.X)),
            (int)Math.Ceiling(polygon.Max(p => p.Y))).Intersect(canvas);
        if (bounds.IsEmpty)
        {
            return null;
        }

        var canvasMask = new StrokeCanvas(canvas.Width, canvas.Height);
        Raster.Polygon(canvasMask, polygon, System.Windows.Media.Colors.White, StrokeCombine.Max);
        var mask = new bool[bounds.Width * bounds.Height];
        var any = false;
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                if (ColorUtil.A(canvasMask.Surface.GetPixel(bounds.X + x, bounds.Y + y)) >= 128)
                {
                    mask[(y * bounds.Width) + x] = true;
                    any = true;
                }
            }
        }

        return any ? new SelectionRegion(bounds, mask, [polygon.ToList()], true) : null;
    }

    /// <summary>A region defined by an explicit mask over <paramref name="bounds"/>.</summary>
    public static SelectionRegion FromMask(PixelRect bounds, bool[] mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        return new SelectionRegion(bounds, mask, [], true);
    }

    /// <summary>True when the canvas pixel is selected.</summary>
    public bool Contains(int x, int y)
    {
        if (!Bounds.Contains(x, y))
        {
            return false;
        }

        return Mask is null || Mask[((y - Bounds.Y) * Bounds.Width) + (x - Bounds.X)];
    }

    /// <summary>The complement of this selection within the canvas.</summary>
    public SelectionRegion? Invert(PixelRect canvas)
    {
        var mask = new bool[canvas.Width * canvas.Height];
        var any = false;
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var sel = !Contains(x, y);
                mask[(y * canvas.Width) + x] = sel;
                any |= sel;
            }
        }

        if (!any)
        {
            return null;
        }

        var outlines = new List<IReadOnlyList<Point>>
        {
            new[] { new Point(0, 0), new Point(canvas.Width, 0), new Point(canvas.Width, canvas.Height), new Point(0, canvas.Height) },
        };
        outlines.AddRange(Outlines);
        return new SelectionRegion(canvas, mask, outlines, true);
    }
}
