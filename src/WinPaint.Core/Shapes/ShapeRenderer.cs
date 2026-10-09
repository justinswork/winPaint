using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPaint.Core.Brushes;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Shapes;

/// <summary>Everything needed to rasterize one shape.</summary>
public sealed record ShapeSpec
{
    /// <summary>Shape kind.</summary>
    public ShapeKind Kind { get; init; }

    /// <summary>Bounding box for box-based shapes.</summary>
    public Rect Bounds { get; init; }

    /// <summary>Points for Line (2), Curve (2 + up to 2 handles) and Polygon (n).</summary>
    public IReadOnlyList<Point> Points { get; init; } = [];

    /// <summary>Polygon is closed.</summary>
    public bool Closed { get; init; } = true;

    /// <summary>Outline style.</summary>
    public ShapeStyle Outline { get; init; } = ShapeStyle.Solid;

    /// <summary>Fill style.</summary>
    public ShapeStyle Fill { get; init; } = ShapeStyle.None;

    /// <summary>Outline width (px).</summary>
    public int Size { get; init; } = 2;

    /// <summary>Outline color.</summary>
    public Color OutlineColor { get; init; } = Colors.Black;

    /// <summary>Fill color.</summary>
    public Color FillColor { get; init; } = Colors.White;

    /// <summary>Opacity 0..1.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Seed for textured styles.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Builds the geometry in canvas coordinates.</summary>
    public Geometry BuildGeometry() => Kind switch
    {
        ShapeKind.Line => ShapeGeometry.Line(Points[0], Points[1]),
        ShapeKind.Curve => ShapeGeometry.Curve(Points[0], Points[1], Points.Count > 2 ? Points[2] : null, Points.Count > 3 ? Points[3] : null),
        ShapeKind.Polygon => ShapeGeometry.FromPoints(Points, Closed && Points.Count > 2),
        _ => ShapeGeometry.Build(Kind, Bounds),
    };

    /// <summary>True when the shape can be filled.</summary>
    public bool IsFillable => !ShapeGeometry.IsOpen(Kind) && !(Kind == ShapeKind.Polygon && (!Closed || Points.Count < 3));
}

/// <summary>Rasterizes shapes into floating pixels.</summary>
public static class ShapeRenderer
{
    /// <summary>
    /// Renders the shape clipped to the canvas. Returns pixels and their canvas offset, or null when empty.
    /// Must run on an STA thread (uses WPF rendering for solid styles).
    /// </summary>
    public static (PixelBuffer Pixels, int X, int Y)? Render(ShapeSpec spec, int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Points.Count == 0 && (spec.Bounds.IsEmpty || spec.Bounds.Width < 0.5 || spec.Bounds.Height < 0.5) && spec.Kind is not (ShapeKind.Line or ShapeKind.Curve or ShapeKind.Polygon))
        {
            return null;
        }

        var geometry = spec.BuildGeometry();
        var b = geometry.Bounds;
        if (b.IsEmpty)
        {
            return null;
        }

        var pad = spec.Size + 4;
        var area = PixelRect.FromRect(new Rect(b.X - pad, b.Y - pad, b.Width + (2 * pad), b.Height + (2 * pad))).Intersect(new PixelRect(0, 0, canvasWidth, canvasHeight));
        if (area.IsEmpty)
        {
            return null;
        }

        var result = new PixelBuffer(area.Width, area.Height);
        if (spec.Fill != ShapeStyle.None && spec.IsFillable)
        {
            var mask = RenderWpf(area, dc => dc.DrawGeometry(System.Windows.Media.Brushes.White, null, geometry));
            var fill = spec.Fill == ShapeStyle.Solid ? Colorize(mask, spec.FillColor, null) : Colorize(mask, spec.FillColor, Texture(spec.Fill, spec.Seed, area));
            result.DrawOver(fill, 0, 0);
        }

        if (spec.Outline != ShapeStyle.None)
        {
            PixelBuffer outline;
            if (spec.Outline == ShapeStyle.Solid)
            {
                var pen = new Pen(new SolidColorBrush(spec.OutlineColor), spec.Size)
                {
                    LineJoin = spec.Kind is ShapeKind.Rectangle ? PenLineJoin.Miter : PenLineJoin.Round,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                pen.Freeze();
                outline = RenderWpf(area, dc => dc.DrawGeometry(null, pen, geometry));
            }
            else
            {
                outline = BrushOutline(spec, geometry, area, canvasWidth, canvasHeight);
            }

            result.DrawOver(outline, 0, 0);
        }

        if (spec.Opacity < 1)
        {
            var o = (int)Math.Round(Math.Clamp(spec.Opacity, 0, 1) * 255);
            for (var i = 0; i < result.Pixels.Length; i++)
            {
                result.Pixels[i] = ColorUtil.Scale(result.Pixels[i], o);
            }
        }

        return (result, area.X, area.Y);
    }

    private static PixelBuffer RenderWpf(PixelRect area, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-area.X, -area.Y));
            draw(dc);
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var buf = new PixelBuffer(area.Width, area.Height);
        rtb.CopyPixels(buf.Pixels, area.Width * 4, 0);
        return buf;
    }

    private static PixelBuffer Colorize(PixelBuffer mask, Color color, Func<int, int, double>? texture)
    {
        var result = new PixelBuffer(mask.Width, mask.Height);
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var a = ColorUtil.A(mask[x, y]) / 255.0;
                if (a <= 0)
                {
                    continue;
                }

                if (texture is not null)
                {
                    a *= texture(x, y);
                }

                result[x, y] = ColorUtil.FromColor(color, a);
            }
        }

        return result;
    }

    private static Func<int, int, double> Texture(ShapeStyle style, int seed, PixelRect area) => style switch
    {
        ShapeStyle.Crayon => (x, y) => Grain(area.X + x, area.Y + y, seed, 0.35),
        ShapeStyle.Marker => (_, _) => 0.5,
        ShapeStyle.Oil => (x, y) => 0.6 + (0.4 * Raster.Noise((area.X + x) / 3, area.Y + y, seed)),
        ShapeStyle.NaturalPencil => (x, y) => 0.25 + (0.5 * Grain(area.X + x, area.Y + y, seed, 0.2)),
        ShapeStyle.Watercolor => (x, y) => 0.3 + (0.1 * Raster.Noise(area.X + x, area.Y + y, seed)),
        _ => (_, _) => 1,
    };

    private static double Grain(int x, int y, int seed, double threshold)
    {
        var n = (Raster.Noise(x, y, seed) * 0.6) + (Raster.Noise(x >> 1, y >> 1, seed + 7) * 0.4);
        return n < threshold ? 0 : Math.Min(1, (n - threshold) / (1 - threshold) * 1.8);
    }

    private static PixelBuffer BrushOutline(ShapeSpec spec, Geometry geometry, PixelRect area, int canvasWidth, int canvasHeight)
    {
        var kind = spec.Outline switch
        {
            ShapeStyle.Crayon => BrushKind.Crayon,
            ShapeStyle.Marker => BrushKind.Marker,
            ShapeStyle.Oil => BrushKind.Oil,
            ShapeStyle.NaturalPencil => BrushKind.NaturalPencil,
            _ => BrushKind.Watercolor,
        };
        var canvas = new StrokeCanvas(canvasWidth, canvasHeight);
        foreach (var (pts, closed) in ShapeGeometry.Flatten(geometry))
        {
            if (pts.Count == 0)
            {
                continue;
            }

            var engine = BrushEngine.Create(kind, spec.OutlineColor, spec.Size, spec.Seed);
            engine.Begin(canvas, pts[0]);
            foreach (var p in pts.Skip(1))
            {
                engine.MoveTo(canvas, p);
            }

            if (closed)
            {
                engine.MoveTo(canvas, pts[0]);
            }
        }

        return canvas.Surface.ToPixelBuffer(area);
    }
}
