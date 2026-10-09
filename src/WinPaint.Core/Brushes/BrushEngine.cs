using System.Windows;
using System.Windows.Media;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Brushes;

/// <summary>The brushes in the Brushes dropdown.</summary>
public enum BrushKind
{
    /// <summary>Round soft-edged brush.</summary>
    Brush,

    /// <summary>Flat nib at 45°.</summary>
    CalligraphyBrush,

    /// <summary>Flat nib at −45°.</summary>
    CalligraphyPen,

    /// <summary>Timer-driven spray.</summary>
    Airbrush,

    /// <summary>Textured streaky oil brush.</summary>
    Oil,

    /// <summary>Grainy crayon.</summary>
    Crayon,

    /// <summary>Semi-transparent flat marker.</summary>
    Marker,

    /// <summary>Thin grainy pencil.</summary>
    NaturalPencil,

    /// <summary>Soft translucent watercolor with wet edges.</summary>
    Watercolor,
}

/// <summary>Draws one stroke into a <see cref="StrokeCanvas"/>. Randomness is seeded for reproducible output.</summary>
public abstract class BrushEngine
{
    private Point _pathPos;
    private Point _lastStamp;
    private double _sinceStamp;

    /// <summary>Creates an engine.</summary>
    protected BrushEngine(Color color, int size, int seed)
    {
        Color = color;
        Size = Math.Clamp(size, 1, 100);
        Random = new Random(seed);
        Seed = seed;
    }

    /// <summary>Stroke color (straight alpha).</summary>
    public Color Color { get; }

    /// <summary>Brush size in px.</summary>
    public int Size { get; }

    /// <summary>Seeded random source.</summary>
    protected Random Random { get; }

    /// <summary>Seed used for textures.</summary>
    protected int Seed { get; }

    /// <summary>True when the engine wants periodic <see cref="Tick"/> calls while the pointer is held.</summary>
    public virtual bool NeedsTimer => false;

    /// <summary>Distance between interpolated stamps.</summary>
    protected virtual double Spacing => Math.Max(1.0, Size * 0.12);

    /// <summary>Current pointer position.</summary>
    protected Point Current { get; private set; }

    /// <summary>Creates the engine for a brush kind.</summary>
    public static BrushEngine Create(BrushKind kind, Color color, int size, int seed) => kind switch
    {
        BrushKind.CalligraphyBrush => new CalligraphyEngine(color, size, seed, -45),
        BrushKind.CalligraphyPen => new CalligraphyEngine(color, size, seed, 45),
        BrushKind.Airbrush => new AirbrushEngine(color, size, seed),
        BrushKind.Oil => new OilEngine(color, size, seed),
        BrushKind.Crayon => new CrayonEngine(color, size, seed),
        BrushKind.Marker => new MarkerEngine(color, size, seed),
        BrushKind.NaturalPencil => new NaturalPencilEngine(color, size, seed),
        BrushKind.Watercolor => new WatercolorEngine(color, size, seed),
        _ => new RoundBrushEngine(color, size, seed),
    };

    /// <summary>Starts the stroke.</summary>
    public virtual void Begin(StrokeCanvas canvas, Point p)
    {
        _pathPos = p;
        _lastStamp = p;
        _sinceStamp = 0;
        Current = p;
        Stamp(canvas, p, p);
    }

    /// <summary>Extends the stroke to <paramref name="p"/>, interpolating stamps so fast strokes have no gaps.</summary>
    public virtual void MoveTo(StrokeCanvas canvas, Point p)
    {
        Current = p;
        var v = p - _pathPos;
        var len = v.Length;
        if (len < 1e-9)
        {
            return;
        }

        var dir = v / len;
        var t = Spacing - _sinceStamp;
        while (t <= len)
        {
            var q = _pathPos + (dir * t);
            Stamp(canvas, q, _lastStamp);
            _lastStamp = q;
            t += Spacing;
        }

        _sinceStamp = len - (t - Spacing);
        _pathPos = p;
        SegmentDone(canvas, p);
    }
    /// <summary>Called periodically while the pointer is held (airbrush).</summary>
    public virtual void Tick(StrokeCanvas canvas)
    {
    }

    /// <summary>Draws a stamp at <paramref name="p"/> (coming from <paramref name="from"/>).</summary>
    protected abstract void Stamp(StrokeCanvas canvas, Point p, Point from);

    /// <summary>Hook after a move was processed.</summary>
    protected virtual void SegmentDone(StrokeCanvas canvas, Point p)
    {
    }

    /// <summary>Color with alpha multiplied.</summary>
    protected Color WithAlpha(double factor) => Color.FromArgb((byte)Math.Clamp(Math.Round(Color.A * factor), 0, 255), Color.R, Color.G, Color.B);

    /// <summary>Paper-grain texture 0..1.</summary>
    protected double Grain(int x, int y, double threshold)
    {
        var n = (Raster.Noise(x, y, Seed) * 0.6) + (Raster.Noise(x >> 1, y >> 1, Seed + 7) * 0.4);
        return n < threshold ? 0 : Math.Min(1, (n - threshold) / (1 - threshold) * 1.8);
    }
}

/// <summary>Round soft-edged antialiased brush.</summary>
internal sealed class RoundBrushEngine(Color color, int size, int seed) : BrushEngine(color, size, seed)
{
    protected override void Stamp(StrokeCanvas canvas, Point p, Point from) =>
        Raster.Disk(canvas, p, Size / 2.0, Color, StrokeCombine.Max, softness: Math.Max(1.0, Size * 0.15));
}

/// <summary>Flat nib; the stroke is thin when moving along the nib and wide across it.</summary>
internal sealed class CalligraphyEngine(Color color, int size, int seed, double angleDeg) : BrushEngine(color, size, seed)
{
    private readonly Vector _nib = new(Math.Cos(angleDeg * Math.PI / 180) * size / 2.0, Math.Sin(angleDeg * Math.PI / 180) * size / 2.0);

    protected override double Spacing => 1.0;

    protected override void Stamp(StrokeCanvas canvas, Point p, Point from)
    {
        var t = new Vector(-_nib.Y, _nib.X);
        t.Normalize();
        t *= 0.6;
        Point[] quad = from == p
            ? [p - _nib - t, p + _nib - t, p + _nib + t, p - _nib + t]
            : [from - _nib, from + _nib, p + _nib, p - _nib];
        Raster.Polygon(canvas, quad, Color, StrokeCombine.Max);
        Point[] cap = [p - _nib - t, p + _nib - t, p + _nib + t, p - _nib + t];
        Raster.Polygon(canvas, cap, Color, StrokeCombine.Max);
    }
}

/// <summary>Random spray dots that keep building while held still.</summary>
internal sealed class AirbrushEngine(Color color, int size, int seed) : BrushEngine(color, size, seed)
{
    public override bool NeedsTimer => true;

    protected override double Spacing => Math.Max(2.0, Size * 0.5);

    public override void Tick(StrokeCanvas canvas) => Spray(canvas, Current);

    protected override void Stamp(StrokeCanvas canvas, Point p, Point from) => Spray(canvas, p);

    private void Spray(StrokeCanvas canvas, Point p)
    {
        var r = Math.Max(2.0, Size * 1.5);
        var count = (int)Math.Max(4, r * 0.8);
        var c = ColorUtil.FromColor(Color);
        for (var i = 0; i < count; i++)
        {
            var a = Random.NextDouble() * Math.PI * 2;
            var d = Math.Sqrt(Random.NextDouble()) * r;
            canvas.Plot((int)Math.Floor(p.X + (Math.Cos(a) * d)), (int)Math.Floor(p.Y + (Math.Sin(a) * d)), c, StrokeCombine.Over);
        }
    }
}

/// <summary>Streaky bristle brush with slight color variation that fades as paint runs out.</summary>
internal sealed class OilEngine : BrushEngine
{
    private readonly (Vector Offset, double Alpha, double Shade, double Radius)[] _bristles;
    private double _travel;

    public OilEngine(Color color, int size, int seed)
        : base(color, size, seed)
    {
        var n = Math.Max(5, size);
        _bristles = new (Vector, double, double, double)[n];
        for (var i = 0; i < n; i++)
        {
            var a = Random.NextDouble() * Math.PI * 2;
            var d = Math.Sqrt(Random.NextDouble()) * size / 2.0;
            _bristles[i] = (new Vector(Math.Cos(a) * d, Math.Sin(a) * d), 0.55 + (Random.NextDouble() * 0.45), 0.85 + (Random.NextDouble() * 0.3), Math.Max(0.6, size / 12.0));
        }
    }

    protected override double Spacing => 1.0;

    protected override void Stamp(StrokeCanvas canvas, Point p, Point from)
    {
        _travel += (p - from).Length;
        var fade = Math.Max(0.45, 1 - (_travel / (400.0 + (Size * 20))));
        foreach (var (offset, alpha, shade, radius) in _bristles)
        {
            var c = Color.FromArgb((byte)(Color.A * alpha * fade), Shade(Color.R, shade), Shade(Color.G, shade), Shade(Color.B, shade));
            Raster.Disk(canvas, p + offset, radius, c, StrokeCombine.Max, 1.0);
        }
    }

    private static byte Shade(byte v, double f) => (byte)Math.Clamp(v * f, 0, 255);
}

/// <summary>Grainy crayon with paper-texture dropout.</summary>
internal sealed class CrayonEngine(Color color, int size, int seed) : BrushEngine(color, size, seed)
{
    protected override void Stamp(StrokeCanvas canvas, Point p, Point from) =>
        Raster.Disk(canvas, p, Math.Max(1.0, Size / 2.0), Color, StrokeCombine.Max, 1.0, (x, y) => Grain(x, y, 0.35));
}

/// <summary>Semi-transparent flat marker that does not build up within one stroke.</summary>
internal sealed class MarkerEngine(Color color, int size, int seed) : BrushEngine(color, size, seed)
{
    protected override void Stamp(StrokeCanvas canvas, Point p, Point from)
    {
        var h = Math.Max(1.0, Size / 2.0);
        var w = Math.Max(1.0, Size / 3.0);
        Point[] rect = [new(p.X - w, p.Y - h), new(p.X + w, p.Y - h), new(p.X + w, p.Y + h), new(p.X - w, p.Y + h)];
        Raster.Polygon(canvas, rect, WithAlpha(0.5), StrokeCombine.Max);
    }
}

/// <summary>Thin, grainy, slightly gray pencil.</summary>
internal sealed class NaturalPencilEngine(Color color, int size, int seed) : BrushEngine(color, size, seed)
{
    private readonly Color _gray = Color.FromArgb(
        (byte)(color.A * 0.85),
        (byte)((color.R * 0.8) + (128 * 0.2)),
        (byte)((color.G * 0.8) + (128 * 0.2)),
        (byte)((color.B * 0.8) + (128 * 0.2)));

    protected override double Spacing => Math.Max(0.7, Size * 0.1);

    protected override void Stamp(StrokeCanvas canvas, Point p, Point from) =>
        Raster.Disk(canvas, p, Math.Max(0.6, Size / 4.0), _gray, StrokeCombine.Max, 0.8, (x, y) => 0.35 + (0.65 * Grain(x, y, 0.2)));
}

/// <summary>Soft translucent brush whose rim is darker than its center (wet edges).</summary>
internal sealed class WatercolorEngine(Color color, int size, int seed) : BrushEngine(color, size, seed)
{
    protected override void Stamp(StrokeCanvas canvas, Point p, Point from)
    {
        var r = Math.Max(1.0, Size / 2.0);
        var x0 = (int)Math.Floor(p.X - r - 1);
        var y0 = (int)Math.Floor(p.Y - r - 1);
        for (var y = y0; y <= (int)Math.Ceiling(p.Y + r + 1); y++)
        {
            for (var x = x0; x <= (int)Math.Ceiling(p.X + r + 1); x++)
            {
                var dx = x + 0.5 - p.X;
                var dy = y + 0.5 - p.Y;
                var d = Math.Sqrt((dx * dx) + (dy * dy)) / r;
                if (d > 1.0)
                {
                    continue;
                }

                var rim = Math.Pow(d, 4) * 0.25;
                var edge = Math.Clamp((1.0 - d) * r, 0, 1);
                var a = (0.22 + rim) * edge * (0.85 + (0.15 * Raster.Noise(x, y, Seed)));
                canvas.PlotCoverage(x, y, Color, a, StrokeCombine.Max);
            }
        }
    }
}
