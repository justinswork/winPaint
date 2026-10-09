using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPaint.Core.Text;
using WinPaint.Core.Tools;

namespace WinPaint.App.Controls;

/// <summary>Draws the neutral surround, the canvas shadow and the transparency checkerboard.</summary>
internal sealed class SurroundLayer(CanvasView owner) : FrameworkElement
{
    private static readonly Brush Checker = CreateChecker();

    protected override void OnRender(DrawingContext dc)
    {
        var bg = (Brush?)TryFindResource("CanvasSurroundBrush") ?? Brushes.LightGray;
        dc.DrawRectangle(bg, null, new Rect(RenderSize));
        var doc = owner.Document;
        if (doc is null)
        {
            return;
        }

        var vt = owner.Transform;
        var r = new Rect(vt.CanvasToView(new Point(0, 0)), vt.CanvasToView(new Point(doc.Width, doc.Height)));
        var shadow = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        dc.DrawRectangle(shadow, null, new Rect(r.X + 2, r.Y + 2, r.Width, r.Height));
        dc.DrawRectangle(Checker, null, r);
    }

    private static DrawingBrush CreateChecker()
    {
        var g = new GeometryGroup();
        g.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        g.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), null, g));
        var b = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        b.Freeze();
        return b;
    }
}

/// <summary>Draws gridlines, selection ants, handles, rubber bands, hover outlines and canvas-resize HandleBox.</summary>
internal sealed class OverlayLayer(CanvasView owner) : FrameworkElement
{
    /// <summary>Marching-ants animation phase.</summary>
    public int AntsPhase { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var doc = owner.Document;
        if (doc is null)
        {
            return;
        }

        var vt = owner.Transform;
        var dpi = vt.DpiScale;
        var px = 1.0 / dpi;
        var canvasRect = new Rect(vt.CanvasToView(new Point(0, 0)), vt.CanvasToView(new Point(doc.Width, doc.Height)));

        if (owner.ShowGridlines && vt.Zoom >= CanvasView.GridZoomThreshold)
        {
            DrawGrid(dc, doc.Width, doc.Height, canvasRect, px);
        }

        // Canvas resize HandleBox.
        var handleFill = (Brush?)TryFindResource("HandleFillBrush") ?? Brushes.White;
        var handleStroke = new Pen((Brush?)TryFindResource("HandleStrokeBrush") ?? Brushes.Gray, px);
        foreach (var (_, rect) in owner.CanvasResizeHandles())
        {
            dc.DrawRectangle(handleFill, handleStroke, rect);
        }

        if (owner.ResizePreview is { } size)
        {
            var r = new Rect(canvasRect.TopLeft, vt.CanvasToView(new Point(size.Width, size.Height)));
            DrawAntsRect(dc, r, px);
        }

        var overlay = owner.Controller?.Overlay;
        if (overlay is not null)
        {
            if (overlay.DashedRect is { } dr)
            {
                DrawAntsRect(dc, ToView(dr), px);
            }

            if (overlay.Ants is { } ants)
            {
                foreach (var poly in ants.Where(a => a.Count > 1))
                {
                    DrawAnts(dc, poly.Select(vt.CanvasToView).ToList(), closed: true, px);
                }
            }

            if (overlay.AntsOpen is { Count: > 1 } open)
            {
                DrawAnts(dc, open.Select(vt.CanvasToView).ToList(), closed: false, px);
            }

            if (overlay.HandleBox is { } hb)
            {
                var r = ToView(hb);
                DrawAntsRect(dc, r, px);
                DrawHandles(dc, r, handleFill, handleStroke);
            }

            if (overlay.ControlPoints is { } cps)
            {
                foreach (var p in cps)
                {
                    var v = vt.CanvasToView(p);
                    dc.DrawEllipse(handleFill, handleStroke, v, 4, 4);
                }
            }
        }

        var session = owner.Controller?.TextSession;
        if (session is not null)
        {
            var poly = owner.Editor.FrameViewPolygon();
            DrawAnts(dc, poly, closed: true, px);
            DrawHandles(dc, poly, handleFill, handleStroke);
        }
        else if (owner.HoverText is { } hover)
        {
            var box = TextLayoutEngine.EffectiveBox(hover);
            var corners = new[] { box.TopLeft, box.TopRight, box.BottomRight, box.BottomLeft }
                .Select(p => vt.CanvasToView(hover.Transform.Transform(p))).ToList();
            var pen = new Pen((Brush?)TryFindResource("HoverOutlineBrush") ?? Brushes.DodgerBlue, px) { DashStyle = new DashStyle([3, 3], 0) };
            dc.DrawGeometry(null, pen, Poly(corners, true));
        }

        if (owner.CursorNeedsOverlay && owner.PointerCanvas is { } pc)
        {
            var size2 = owner.Controller!.CursorSize * vt.Scale;
            var c = owner.PointerView;
            var outer = new Pen(Brushes.White, 3 * px);
            var inner = new Pen(Brushes.Black, px);
            if (owner.Controller.ActiveToolKind == ToolKind.Eraser)
            {
                var r = new Rect(c.X - (size2 / 2), c.Y - (size2 / 2), size2, size2);
                dc.DrawRectangle(null, outer, r);
                dc.DrawRectangle(null, inner, r);
            }
            else
            {
                dc.DrawEllipse(null, outer, c, size2 / 2, size2 / 2);
                dc.DrawEllipse(null, inner, c, size2 / 2, size2 / 2);
            }

            _ = pc;
        }
    }

    private static StreamGeometry Poly(List<Point> pts, bool closed)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], false, closed);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, false);
        }

        g.Freeze();
        return g;
    }

    private static void DrawHandles(DrawingContext dc, Rect r, Brush fill, Pen stroke)
    {
        foreach (var (_, c) in HandleBox.Positions(r))
        {
            dc.DrawRectangle(fill, stroke, new Rect(c.X - 3.5, c.Y - 3.5, 7, 7));
        }
    }

    private static void DrawHandles(DrawingContext dc, IReadOnlyList<Point> quad, Brush fill, Pen stroke)
    {
        for (var i = 0; i < 4; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % 4];
            foreach (var c in new[] { a, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2) })
            {
                dc.DrawRectangle(fill, stroke, new Rect(c.X - 3.5, c.Y - 3.5, 7, 7));
            }
        }
    }

    private Rect ToView(Rect canvasRect)
    {
        var vt = owner.Transform;
        return new Rect(vt.CanvasToView(canvasRect.TopLeft), vt.CanvasToView(canvasRect.BottomRight));
    }

    private void DrawAntsRect(DrawingContext dc, Rect r, double px) =>
        DrawAnts(dc, [r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft], closed: true, px);

    private void DrawAnts(DrawingContext dc, IEnumerable<Point> pts, bool closed, double px)
    {
        // Two-tone dash so the outline is visible on any image.
        var g = Poly(pts.Select(p => new Point(Math.Round(p.X / px) * px + (px / 2), Math.Round(p.Y / px) * px + (px / 2))).ToList(), closed);
        dc.DrawGeometry(null, new Pen(Brushes.White, px), g);
        dc.DrawGeometry(null, new Pen(Brushes.Black, px) { DashStyle = new DashStyle([4, 4], AntsPhase) }, g);
    }

    private void DrawGrid(DrawingContext dc, int w, int h, Rect canvasRect, double px)
    {
        var vt = owner.Transform;
        var visible = Rect.Intersect(canvasRect, new Rect(RenderSize));
        if (visible.IsEmpty)
        {
            return;
        }

        var a = vt.ViewToCanvas(visible.TopLeft);
        var b = vt.ViewToCanvas(visible.BottomRight);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (var x = Math.Max(1, (int)Math.Floor(a.X)); x <= Math.Min(w - 1, (int)Math.Ceiling(b.X)); x++)
            {
                var vx = Math.Round(vt.CanvasToView(new Point(x, 0)).X / px) * px;
                ctx.BeginFigure(new Point(vx, visible.Top), false, false);
                ctx.LineTo(new Point(vx, visible.Bottom), true, false);
            }

            for (var y = Math.Max(1, (int)Math.Floor(a.Y)); y <= Math.Min(h - 1, (int)Math.Ceiling(b.Y)); y++)
            {
                var vy = Math.Round(vt.CanvasToView(new Point(0, y)).Y / px) * px;
                ctx.BeginFigure(new Point(visible.Left, vy), false, false);
                ctx.LineTo(new Point(visible.Right, vy), true, false);
            }
        }

        g.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)), px), g);
    }
}

/// <summary>Pixel ruler along the top or left edge, with a cursor marker.</summary>
internal sealed class Ruler(CanvasView owner, Orientation orientation) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var bg = (Brush?)TryFindResource("RulerBackgroundBrush") ?? Brushes.WhiteSmoke;
        var fg = (Brush?)TryFindResource("RulerForegroundBrush") ?? Brushes.Gray;
        dc.DrawRectangle(bg, null, new Rect(RenderSize));
        var doc = owner.Document;
        if (doc is null)
        {
            return;
        }

        var vt = owner.Transform;
        var px = 1.0 / vt.DpiScale;
        var horizontal = orientation == Orientation.Horizontal;
        var length = horizontal ? ActualWidth : ActualHeight;
        var thick = horizontal ? ActualHeight : ActualWidth;
        var pen = new Pen(fg, px);
        var scale = vt.Scale;
        double[] steps = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 5000, 10000];
        var major = steps.FirstOrDefault(s => s * scale >= 60, 10000);
        var minor = major / (major % 5 == 0 ? 5 : 2);
        var startCanvas = horizontal ? vt.ViewToCanvas(new Point(0, 0)).X : vt.ViewToCanvas(new Point(0, 0)).Y;
        var endCanvas = horizontal ? vt.ViewToCanvas(new Point(length, 0)).X : vt.ViewToCanvas(new Point(0, length)).Y;
        var first = Math.Floor(startCanvas / minor) * minor;
        var typeface = new Typeface("Segoe UI");
        for (var c = first; c <= endCanvas; c += minor)
        {
            var v = horizontal ? vt.CanvasToView(new Point(c, 0)).X : vt.CanvasToView(new Point(0, c)).Y;
            v = (Math.Round(v / px) * px) + (px / 2);
            var isMajor = Math.Abs(c % major) < 1e-6;
            var tick = isMajor ? thick * 0.6 : thick * 0.25;
            if (horizontal)
            {
                dc.DrawLine(pen, new Point(v, thick), new Point(v, thick - tick));
            }
            else
            {
                dc.DrawLine(pen, new Point(thick, v), new Point(thick - tick, v));
            }

            if (isMajor)
            {
                var ft = new FormattedText(c.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                if (horizontal)
                {
                    dc.DrawText(ft, new Point(v + 2, 0));
                }
                else
                {
                    dc.PushTransform(new RotateTransform(-90, 0, v));
                    dc.DrawText(ft, new Point(2, v - 1 - ft.Height + 12));
                    dc.Pop();
                }
            }
        }

        if (owner.PointerCanvas is { } p)
        {
            var accent = (Brush?)TryFindResource("AccentBrush") ?? Brushes.DodgerBlue;
            var v = horizontal ? vt.CanvasToView(new Point(Math.Floor(p.X) + 0.5, 0)).X : vt.CanvasToView(new Point(0, Math.Floor(p.Y) + 0.5)).Y;
            if (horizontal)
            {
                dc.DrawLine(new Pen(accent, 1), new Point(v, 0), new Point(v, thick));
            }
            else
            {
                dc.DrawLine(new Pen(accent, 1), new Point(0, v), new Point(thick, v));
            }
        }

        var edge = new Pen(fg, px);
        if (horizontal)
        {
            dc.DrawLine(edge, new Point(0, thick - (px / 2)), new Point(length, thick - (px / 2)));
        }
        else
        {
            dc.DrawLine(edge, new Point(thick - (px / 2), 0), new Point(thick - (px / 2), length));
        }
    }
}
