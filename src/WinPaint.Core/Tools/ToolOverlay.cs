using System.Windows;

namespace WinPaint.Core.Tools;

/// <summary>
/// Transient visuals a tool wants drawn above the canvas (in canvas coordinates): marching ants, handles,
/// rubber bands and control points. The view renders them; tools never touch UI types.
/// </summary>
public sealed record ToolOverlay
{
    /// <summary>Closed outlines drawn with marching ants (selection).</summary>
    public IReadOnlyList<IReadOnlyList<Point>>? Ants { get; init; }

    /// <summary>Open polyline drawn with marching ants (lasso in progress).</summary>
    public IReadOnlyList<Point>? AntsOpen { get; init; }

    /// <summary>Box drawn dashed with eight resize handles (floating selection/shape).</summary>
    public Rect? HandleBox { get; init; }

    /// <summary>Plain dashed rectangle (rubber band).</summary>
    public Rect? DashedRect { get; init; }

    /// <summary>Control points (curve/polygon editing).</summary>
    public IReadOnlyList<Point>? ControlPoints { get; init; }
}

/// <summary>Tools that draw overlay visuals.</summary>
public interface IOverlayTool
{
    /// <summary>Current overlay or null.</summary>
    ToolOverlay? Overlay { get; }
}

/// <summary>Resize/move handles around a box.</summary>
public enum HandleKind
{
    /// <summary>No handle.</summary>
    None,

    /// <summary>Inside the box (move).</summary>
    Move,

    /// <summary>Top-left corner.</summary>
    TopLeft,

    /// <summary>Top edge.</summary>
    Top,

    /// <summary>Top-right corner.</summary>
    TopRight,

    /// <summary>Right edge.</summary>
    Right,

    /// <summary>Bottom-right corner.</summary>
    BottomRight,

    /// <summary>Bottom edge.</summary>
    Bottom,

    /// <summary>Bottom-left corner.</summary>
    BottomLeft,

    /// <summary>Left edge.</summary>
    Left,
}

/// <summary>Hit testing and resizing for handle boxes.</summary>
public static class HandleBox
{
    /// <summary>Handle positions for a box (handle kind → center).</summary>
    public static IEnumerable<(HandleKind Kind, Point Center)> Positions(Rect r)
    {
        var cx = r.X + (r.Width / 2);
        var cy = r.Y + (r.Height / 2);
        yield return (HandleKind.TopLeft, r.TopLeft);
        yield return (HandleKind.Top, new Point(cx, r.Top));
        yield return (HandleKind.TopRight, r.TopRight);
        yield return (HandleKind.Right, new Point(r.Right, cy));
        yield return (HandleKind.BottomRight, r.BottomRight);
        yield return (HandleKind.Bottom, new Point(cx, r.Bottom));
        yield return (HandleKind.BottomLeft, r.BottomLeft);
        yield return (HandleKind.Left, new Point(r.Left, cy));
    }

    /// <summary>
    /// Which handle (or the move area) is at <paramref name="p"/>. <paramref name="tolerance"/> is the handle
    /// half-size in canvas pixels (callers convert from screen pixels).
    /// </summary>
    public static HandleKind HitTest(Rect r, Point p, double tolerance)
    {
        foreach (var (kind, c) in Positions(r))
        {
            if (Math.Abs(p.X - c.X) <= tolerance && Math.Abs(p.Y - c.Y) <= tolerance)
            {
                return kind;
            }
        }

        return r.Contains(p) ? HandleKind.Move : HandleKind.None;
    }

    /// <summary>Applies a handle drag to a box. With <paramref name="keepAspect"/> corners keep the aspect ratio.</summary>
    public static Rect Resize(Rect start, HandleKind handle, Vector delta, bool keepAspect, double minSize = 1)
    {
        double l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
        switch (handle)
        {
            case HandleKind.Move:
                return new Rect(start.X + delta.X, start.Y + delta.Y, start.Width, start.Height);
            case HandleKind.TopLeft:
                l += delta.X;
                t += delta.Y;
                break;
            case HandleKind.Top:
                t += delta.Y;
                break;
            case HandleKind.TopRight:
                r += delta.X;
                t += delta.Y;
                break;
            case HandleKind.Right:
                r += delta.X;
                break;
            case HandleKind.BottomRight:
                r += delta.X;
                b += delta.Y;
                break;
            case HandleKind.Bottom:
                b += delta.Y;
                break;
            case HandleKind.BottomLeft:
                l += delta.X;
                b += delta.Y;
                break;
            case HandleKind.Left:
                l += delta.X;
                break;
        }

        if (keepAspect && start.Width > 0 && start.Height > 0 && handle is HandleKind.TopLeft or HandleKind.TopRight or HandleKind.BottomLeft or HandleKind.BottomRight)
        {
            var sx = Math.Abs(r - l) / start.Width;
            var sy = Math.Abs(b - t) / start.Height;
            var s = Math.Max(sx, sy);
            var w = start.Width * s;
            var h = start.Height * s;
            if (handle is HandleKind.TopLeft or HandleKind.BottomLeft)
            {
                l = r - w;
            }
            else
            {
                r = l + w;
            }

            if (handle is HandleKind.TopLeft or HandleKind.TopRight)
            {
                t = b - h;
            }
            else
            {
                b = t + h;
            }
        }

        if (r - l < minSize)
        {
            if (handle is HandleKind.TopLeft or HandleKind.Left or HandleKind.BottomLeft)
            {
                l = r - minSize;
            }
            else
            {
                r = l + minSize;
            }
        }

        if (b - t < minSize)
        {
            if (handle is HandleKind.TopLeft or HandleKind.Top or HandleKind.TopRight)
            {
                t = b - minSize;
            }
            else
            {
                b = t + minSize;
            }
        }

        return new Rect(new Point(l, t), new Point(r, b));
    }
}
