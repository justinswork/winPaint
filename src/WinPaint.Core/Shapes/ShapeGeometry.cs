using System.Windows;
using System.Windows.Media;

namespace WinPaint.Core.Shapes;

/// <summary>Geometry generators for every shape (in canvas coordinates).</summary>
public static class ShapeGeometry
{
    /// <summary>Unit-square polygon definitions for the point-based shapes.</summary>
    private static readonly Dictionary<ShapeKind, Point[]> UnitPolygons = new()
    {
        [ShapeKind.Triangle] = [new(0.5, 0), new(1, 1), new(0, 1)],
        [ShapeKind.RightTriangle] = [new(0, 0), new(1, 1), new(0, 1)],
        [ShapeKind.Diamond] = [new(0.5, 0), new(1, 0.5), new(0.5, 1), new(0, 0.5)],
        [ShapeKind.Pentagon] = RegularPolygon(5, 0),
        [ShapeKind.Hexagon] = [new(0.25, 0), new(0.75, 0), new(1, 0.5), new(0.75, 1), new(0.25, 1), new(0, 0.5)],
        [ShapeKind.RightArrow] = [new(0, 0.25), new(0.5, 0.25), new(0.5, 0), new(1, 0.5), new(0.5, 1), new(0.5, 0.75), new(0, 0.75)],
        [ShapeKind.LeftArrow] = [new(1, 0.25), new(0.5, 0.25), new(0.5, 0), new(0, 0.5), new(0.5, 1), new(0.5, 0.75), new(1, 0.75)],
        [ShapeKind.UpArrow] = [new(0.25, 1), new(0.25, 0.5), new(0, 0.5), new(0.5, 0), new(1, 0.5), new(0.75, 0.5), new(0.75, 1)],
        [ShapeKind.DownArrow] = [new(0.25, 0), new(0.25, 0.5), new(0, 0.5), new(0.5, 1), new(1, 0.5), new(0.75, 0.5), new(0.75, 0)],
        [ShapeKind.FourPointStar] = Star(4, 0.38),
        [ShapeKind.FivePointStar] = Star(5, 0.38),
        [ShapeKind.SixPointStar] = Star(6, 0.5),
        [ShapeKind.Lightning] =
        [
            new(0.38, 0), new(0.62, 0.3), new(0.52, 0.34), new(0.8, 0.6), new(0.7, 0.64), new(1, 1),
            new(0.48, 0.7), new(0.6, 0.66), new(0.22, 0.42), new(0.36, 0.38), new(0, 0.1),
        ],
    };

    /// <summary>True for shapes defined by a fixed polygon.</summary>
    public static bool IsPolygonShape(ShapeKind kind) => UnitPolygons.ContainsKey(kind);

    /// <summary>True for open (non-fillable) shapes.</summary>
    public static bool IsOpen(ShapeKind kind) => kind is ShapeKind.Line or ShapeKind.Curve;

    /// <summary>
    /// Bounding box from a drag. With <paramref name="constrain"/> (Shift) the box becomes a square.
    /// </summary>
    public static Rect DragBounds(Point start, Point end, bool constrain)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (constrain)
        {
            var m = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = Math.Sign(dx == 0 ? 1 : dx) * m;
            dy = Math.Sign(dy == 0 ? 1 : dy) * m;
        }

        return new Rect(start, new Point(start.X + dx, start.Y + dy));
    }

    /// <summary>Snaps a line end point to the nearest multiple of 45° (Shift).</summary>
    public static Point ConstrainLine(Point start, Point end)
    {
        var v = end - start;
        var len = v.Length;
        if (len < 1e-9)
        {
            return end;
        }

        var angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        var proj = Math.Abs(Math.Cos(angle)) > 0.5 && Math.Abs(Math.Sin(angle)) > 0.5
            ? Math.Max(Math.Abs(v.X), Math.Abs(v.Y)) * Math.Sqrt(2)
            : len;
        return new Point(start.X + Math.Round(Math.Cos(angle) * proj), start.Y + Math.Round(Math.Sin(angle) * proj));
    }

    /// <summary>The polygon vertices of a fixed polygon shape mapped into <paramref name="bounds"/>.</summary>
    public static IReadOnlyList<Point> PolygonPoints(ShapeKind kind, Rect bounds)
    {
        if (!UnitPolygons.TryGetValue(kind, out var unit))
        {
            throw new ArgumentException($"{kind} is not a polygon shape.", nameof(kind));
        }

        return unit.Select(p => new Point(bounds.X + (p.X * bounds.Width), bounds.Y + (p.Y * bounds.Height))).ToList();
    }

    /// <summary>Geometry for a box-based shape (everything except Line, Curve, Polygon).</summary>
    public static Geometry Build(ShapeKind kind, Rect bounds)
    {
        Geometry g = kind switch
        {
            ShapeKind.Rectangle => new RectangleGeometry(bounds),
            ShapeKind.RoundedRectangle => new RectangleGeometry(bounds, CornerRadius(bounds), CornerRadius(bounds)),
            ShapeKind.Oval => new EllipseGeometry(bounds),
            ShapeKind.Heart => Heart(bounds),
            ShapeKind.RoundedRectCallout => RoundedCallout(bounds),
            ShapeKind.OvalCallout => OvalCallout(bounds),
            ShapeKind.CloudCallout => FitTo(CloudCallout(bounds), bounds),
            ShapeKind.Line or ShapeKind.Curve or ShapeKind.Polygon => throw new ArgumentException("Use the point-based builders.", nameof(kind)),
            _ => FromPoints(PolygonPoints(kind, bounds), closed: true),
        };
        g.Freeze();
        return g;
    }

    /// <summary>Open or closed polyline geometry.</summary>
    public static Geometry FromPoints(IReadOnlyList<Point> pts, bool closed)
    {
        ArgumentNullException.ThrowIfNull(pts);
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = closed, IsFilled = closed };
        fig.Segments.Add(new PolyLineSegment(pts.Skip(1), true));
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }

    /// <summary>Line from a to b.</summary>
    public static Geometry Line(Point a, Point b) => FromPoints([a, b], closed: false);

    /// <summary>
    /// Curve through the drag's end points bent by up to two control points (Paint's curve tool).
    /// With no control point it is a straight line; with one, both handles use it.
    /// </summary>
    public static Geometry Curve(Point a, Point b, Point? c1, Point? c2)
    {
        var h1 = c1 ?? a;
        var h2 = c2 ?? c1 ?? b;
        var fig = new PathFigure { StartPoint = a, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new BezierSegment(h1, h2, b, true));
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }

    /// <summary>Flattens a geometry into polylines (one per figure) with the figure's closed flag.</summary>
    public static IReadOnlyList<(IReadOnlyList<Point> Points, bool Closed)> Flatten(Geometry g, double tolerance = 0.25)
    {
        ArgumentNullException.ThrowIfNull(g);

        // Open figures with IsFilled=false are dropped by WPF's flattening, so mark every figure filled first
        // (filling is decided separately by the renderer).
        var source = PathGeometry.CreateFromGeometry(g);
        foreach (var fig in source.Figures)
        {
            fig.IsFilled = true;
        }

        var flat = source.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute);
        var result = new List<(IReadOnlyList<Point>, bool)>();
        foreach (var fig in flat.Figures)
        {
            var pts = new List<Point> { fig.StartPoint };
            foreach (var seg in fig.Segments)
            {
                switch (seg)
                {
                    case PolyLineSegment pl:
                        pts.AddRange(pl.Points);
                        break;
                    case LineSegment ls:
                        pts.Add(ls.Point);
                        break;
                }
            }

            result.Add((pts, fig.IsClosed));
        }

        return result;
    }

    private static double CornerRadius(Rect b) => Math.Min(Math.Min(b.Width, b.Height) * 0.15, 40);

    private static Point[] RegularPolygon(int n, double rotation)
    {
        var raw = Enumerable.Range(0, n)
            .Select(i => (-Math.PI / 2) + rotation + (i * 2 * Math.PI / n))
            .Select(a => new Point(Math.Cos(a), Math.Sin(a)))
            .ToArray();
        return Normalize(raw);
    }

    private static Point[] Star(int points, double innerRatio)
    {
        var raw = new Point[points * 2];
        for (var i = 0; i < raw.Length; i++)
        {
            var a = (-Math.PI / 2) + (i * Math.PI / points);
            var r = i % 2 == 0 ? 1.0 : innerRatio;
            raw[i] = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
        }

        return Normalize(raw);
    }

    private static Point[] Normalize(Point[] raw)
    {
        var minX = raw.Min(p => p.X);
        var maxX = raw.Max(p => p.X);
        var minY = raw.Min(p => p.Y);
        var maxY = raw.Max(p => p.Y);
        return raw.Select(p => new Point((p.X - minX) / (maxX - minX), (p.Y - minY) / (maxY - minY))).ToArray();
    }

    private static Point Map(Rect b, double ux, double uy) => new(b.X + (ux * b.Width), b.Y + (uy * b.Height));

    private static PathGeometry Heart(Rect b)
    {
        var fig = new PathFigure { StartPoint = Map(b, 0.5, 0.25), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new BezierSegment(Map(b, 0.5, 0.0), Map(b, 0.0, 0.0), Map(b, 0.0, 0.3), true));
        fig.Segments.Add(new BezierSegment(Map(b, 0.0, 0.6), Map(b, 0.35, 0.75), Map(b, 0.5, 1.0), true));
        fig.Segments.Add(new BezierSegment(Map(b, 0.65, 0.75), Map(b, 1.0, 0.6), Map(b, 1.0, 0.3), true));
        fig.Segments.Add(new BezierSegment(Map(b, 1.0, 0.0), Map(b, 0.5, 0.0), Map(b, 0.5, 0.25), true));
        return new PathGeometry([fig]);
    }

    private static Geometry RoundedCallout(Rect b)
    {
        var body = new Rect(b.X, b.Y, b.Width, b.Height * 0.75);
        var rect = new RectangleGeometry(body, CornerRadius(body), CornerRadius(body));
        var tail = FromPoints([Map(b, 0.2, 0.7), Map(b, 0.15, 1.0), Map(b, 0.4, 0.7)], true);
        return Union(rect, tail);
    }

    private static Geometry OvalCallout(Rect b)
    {
        var body = new Rect(b.X, b.Y, b.Width, b.Height * 0.8);
        var ell = new EllipseGeometry(body);
        var tail = FromPoints([Map(b, 0.22, 0.6), Map(b, 0.12, 1.0), Map(b, 0.42, 0.75)], true);
        return Union(ell, tail);
    }

    private static Geometry CloudCallout(Rect b)
    {
        var body = new Rect(b.X, b.Y, b.Width, b.Height * 0.78);
        (double X, double Y, double R)[] puffs =
        [
            (0.25, 0.35, 0.22), (0.45, 0.22, 0.24), (0.68, 0.25, 0.22), (0.82, 0.5, 0.2),
            (0.65, 0.72, 0.22), (0.4, 0.75, 0.22), (0.18, 0.6, 0.2), (0.5, 0.5, 0.3),
        ];
        Geometry acc = Geometry.Empty;
        foreach (var (x, y, r) in puffs)
        {
            var c = new Point(body.X + (x * body.Width), body.Y + (y * body.Height));
            acc = Union(acc, new EllipseGeometry(c, r * body.Width, r * body.Height * 1.1));
        }

        // Trailing thought bubbles.
        acc = Union(acc, new EllipseGeometry(Map(b, 0.2, 0.86), b.Width * 0.05, b.Height * 0.05));
        acc = Union(acc, new EllipseGeometry(Map(b, 0.1, 0.96), b.Width * 0.03, b.Height * 0.03));
        return acc;
    }

    /// <summary>Scales and moves a geometry so its bounds equal <paramref name="target"/>.</summary>
    private static Geometry FitTo(Geometry g, Rect target)
    {
        var b = g.Bounds;
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0)
        {
            return g;
        }

        var m = Matrix.Identity;
        m.Translate(-b.X, -b.Y);
        m.Scale(target.Width / b.Width, target.Height / b.Height);
        m.Translate(target.X, target.Y);
        var fitted = g.CloneCurrentValue();
        fitted.Transform = new MatrixTransform(m);
        return fitted.GetFlattenedPathGeometry(0.1, ToleranceType.Absolute);
    }

    private static Geometry Union(Geometry a, Geometry b) =>
        a.IsEmpty() ? b : new CombinedGeometry(GeometryCombineMode.Union, a, b).GetFlattenedPathGeometry(0.1, ToleranceType.Absolute);
}
