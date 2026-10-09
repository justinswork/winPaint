using System.Windows;

namespace WinPaint.Core.View;

/// <summary>
/// The single mapping between canvas pixels and view coordinates (WPF DIPs relative to the viewport).
/// At 100 % zoom one canvas pixel covers exactly one physical screen pixel (scale = zoom / dpiScale).
/// </summary>
public sealed class ViewTransform
{
    /// <summary>Minimum zoom (1 %).</summary>
    public const double MinZoom = 0.01;

    /// <summary>Maximum zoom (800 %).</summary>
    public const double MaxZoom = 8.0;

    /// <summary>Preset zoom steps used by zoom in/out commands.</summary>
    public static IReadOnlyList<double> Presets { get; } = [0.125, 0.25, 0.5, 1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>Zoom factor (1 = 100 %).</summary>
    public double Zoom { get; set; } = 1;

    /// <summary>Physical pixels per DIP of the monitor (1.0 at 96 DPI, 1.5 at 144 DPI…).</summary>
    public double DpiScale { get; set; } = 1;

    /// <summary>Horizontal scroll offset in DIPs.</summary>
    public double ScrollX { get; set; }

    /// <summary>Vertical scroll offset in DIPs.</summary>
    public double ScrollY { get; set; }

    /// <summary>Offset of the canvas origin inside the scrollable content, in DIPs (margin around the canvas).</summary>
    public Vector Origin { get; set; }

    /// <summary>DIPs per canvas pixel.</summary>
    public double Scale => Zoom / DpiScale;

    /// <summary>Canvas pixel coordinates → view DIPs.</summary>
    public Point CanvasToView(Point c) => new((c.X * Scale) + Origin.X - ScrollX, (c.Y * Scale) + Origin.Y - ScrollY);

    /// <summary>View DIPs → canvas pixel coordinates (fractional).</summary>
    public Point ViewToCanvas(Point v) => new((v.X + ScrollX - Origin.X) / Scale, (v.Y + ScrollY - Origin.Y) / Scale);

    /// <summary>View DIPs → the canvas pixel containing the point.</summary>
    public (int X, int Y) ViewToPixel(Point v)
    {
        var c = ViewToCanvas(v);
        return ((int)Math.Floor(c.X + 1e-9), (int)Math.Floor(c.Y + 1e-9));
    }

    /// <summary>
    /// Changes the zoom keeping the canvas point under <paramref name="anchorView"/> fixed on screen.
    /// </summary>
    public void ZoomAt(double newZoom, Point anchorView)
    {
        var c = ViewToCanvas(anchorView);
        Zoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        var after = CanvasToView(c);
        ScrollX += after.X - anchorView.X;
        ScrollY += after.Y - anchorView.Y;
    }

    /// <summary>Next preset above the current zoom.</summary>
    public static double NextPreset(double zoom)
    {
        foreach (var p in Presets)
        {
            if (p > zoom + 1e-6)
            {
                return p;
            }
        }

        return MaxZoom;
    }

    /// <summary>Next preset below the current zoom.</summary>
    public static double PreviousPreset(double zoom)
    {
        for (var i = Presets.Count - 1; i >= 0; i--)
        {
            if (Presets[i] < zoom - 1e-6)
            {
                return Presets[i];
            }
        }

        return Presets[0];
    }

    /// <summary>Zoom that fits a canvas into a viewport (never above 100 %, like Paint).</summary>
    public static double FitZoom(int canvasW, int canvasH, double viewW, double viewH, double dpiScale, double margin = 20)
    {
        var sx = (viewW - (2 * margin)) * dpiScale / Math.Max(1, canvasW);
        var sy = (viewH - (2 * margin)) * dpiScale / Math.Max(1, canvasH);
        return Math.Clamp(Math.Min(Math.Min(sx, sy), 1.0), MinZoom, MaxZoom);
    }
}
