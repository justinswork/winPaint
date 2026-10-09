using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Brushes;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Tools;

/// <summary>Base class for tools without floating state.</summary>
public abstract class ToolBase(IToolHost host) : ITool
{
    /// <summary>Host services.</summary>
    protected IToolHost Host { get; } = host;

    /// <inheritdoc/>
    public abstract ToolKind Kind { get; }

    /// <inheritdoc/>
    public virtual bool HasPendingOperation => false;

    /// <inheritdoc/>
    public abstract ToolCursor CursorAt(Point canvasPoint);

    /// <inheritdoc/>
    public virtual void Activate()
    {
    }

    /// <inheritdoc/>
    public virtual void Deactivate() => CommitPending();

    /// <inheritdoc/>
    public virtual void CommitPending()
    {
    }

    /// <inheritdoc/>
    public virtual void OnPointerDown(PointerInput input)
    {
    }

    /// <inheritdoc/>
    public virtual void OnPointerMove(PointerInput input)
    {
    }

    /// <inheritdoc/>
    public virtual void OnPointerUp(PointerInput input)
    {
    }

    /// <inheritdoc/>
    public virtual bool OnKey(Key key, ModifierKeys modifiers) => false;

    /// <inheritdoc/>
    public virtual void OnTimer()
    {
    }

    /// <summary>Color for a mouse button (left = primary, right = secondary).</summary>
    protected Color ColorFor(PointerButton b) => b == PointerButton.Right ? Host.Settings.Secondary : Host.Settings.Primary;
}

/// <summary>Base for tools that paint a stroke while a button is held.</summary>
public abstract class StrokeToolBase(IToolHost host) : ToolBase(host)
{
    /// <summary>The active stroke.</summary>
    protected StrokeSession? Session { get; set; }

    /// <summary>Button that started the stroke.</summary>
    protected PointerButton StrokeButton { get; private set; }

    /// <summary>Undo step name.</summary>
    protected abstract string StepName { get; }

    /// <inheritdoc/>
    public override bool HasPendingOperation => Session is not null;

    /// <inheritdoc/>
    public override void OnPointerDown(PointerInput input)
    {
        if (input.Button is not (PointerButton.Left or PointerButton.Right))
        {
            return;
        }

        if (Session is not null)
        {
            // Pressing the other button mid-stroke cancels it, like Paint.
            Session.Cancel();
            Session = null;
            return;
        }

        StrokeButton = input.Button;
        Session = BeginStroke(input);
        Session.Flush();
    }

    /// <inheritdoc/>
    public override void OnPointerMove(PointerInput input)
    {
        if (Session is null)
        {
            return;
        }

        ContinueStroke(input);
        Session.Flush();
    }

    /// <inheritdoc/>
    public override void OnPointerUp(PointerInput input)
    {
        if (Session is null || input.Button != StrokeButton)
        {
            return;
        }

        ContinueStroke(input);
        Session.Commit(StepName);
        Session = null;
    }

    /// <inheritdoc/>
    public override void CommitPending()
    {
        if (Session is not null)
        {
            Session.Commit(StepName);
            Session = null;
        }
    }

    /// <inheritdoc/>
    public override bool OnKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Escape && Session is not null)
        {
            Session.Cancel();
            Session = null;
            return true;
        }

        return false;
    }

    /// <summary>Creates the session and draws the first stamp.</summary>
    protected abstract StrokeSession BeginStroke(PointerInput input);

    /// <summary>Extends the stroke.</summary>
    protected abstract void ContinueStroke(PointerInput input);
}

/// <summary>Hard-edged aliased pencil. Shift constrains to horizontal, vertical or 45°.</summary>
public sealed class PencilTool(IToolHost host) : StrokeToolBase(host)
{
    private (int X, int Y) _start;
    private (int X, int Y) _last;
    private uint _color;
    private int _size;

    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Pencil;

    /// <inheritdoc/>
    protected override string StepName => "Pencil";

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint) => ToolCursor.Pencil;

    /// <inheritdoc/>
    protected override StrokeSession BeginStroke(PointerInput input)
    {
        var s = new StrokeSession(Host.Document, StrokeMode.Paint, Host.Settings.Opacity);
        _color = ColorUtil.FromColor(ColorFor(input.Button));
        _size = Host.Settings.PencilSize;
        _start = _last = input.Pixel;
        Plot(s.Canvas, _start.X, _start.Y);
        return s;
    }

    /// <inheritdoc/>
    protected override void ContinueStroke(PointerInput input)
    {
        var p = input.Pixel;
        var canvas = Session!.Canvas;
        if (input.Shift)
        {
            canvas.Clear();
            p = Constrain(_start, p);
            Raster.Bresenham(_start.X, _start.Y, p.X, p.Y, (x, y) => Plot(canvas, x, y));
        }
        else
        {
            Raster.Bresenham(_last.X, _last.Y, p.X, p.Y, (x, y) => Plot(canvas, x, y));
        }

        _last = p;
    }

    /// <summary>Snaps the end point to horizontal, vertical or 45° from the start.</summary>
    public static (int X, int Y) Constrain((int X, int Y) start, (int X, int Y) p)
    {
        var dx = p.X - start.X;
        var dy = p.Y - start.Y;
        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);
        if (ax > 2 * ay)
        {
            return (p.X, start.Y);
        }

        if (ay > 2 * ax)
        {
            return (start.X, p.Y);
        }

        var m = Math.Max(ax, ay);
        return (start.X + (Math.Sign(dx) * m), start.Y + (Math.Sign(dy) * m));
    }

    private void Plot(StrokeCanvas c, int x, int y) => Raster.HardDisk(c, new Point(x + 0.5, y + 0.5), _size, _color);
}

/// <summary>All brushes from the Brushes dropdown.</summary>
public sealed class BrushTool(IToolHost host) : StrokeToolBase(host)
{
    private BrushEngine? _engine;

    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Brush;

    /// <inheritdoc/>
    protected override string StepName => "Brush stroke";

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint) => ToolCursor.BrushCircle;

    /// <inheritdoc/>
    public override void OnTimer()
    {
        if (Session is not null && _engine is { NeedsTimer: true })
        {
            _engine.Tick(Session.Canvas);
            Session.Flush();
        }
    }

    /// <inheritdoc/>
    protected override StrokeSession BeginStroke(PointerInput input)
    {
        var s = new StrokeSession(Host.Document, StrokeMode.Paint, Host.Settings.Opacity);
        _engine = BrushEngine.Create(Host.Settings.Brush, ColorFor(input.Button), Host.Settings.BrushSize, Host.NextSeed());
        _engine.Begin(s.Canvas, input.Position);
        return s;
    }

    /// <inheritdoc/>
    protected override void ContinueStroke(PointerInput input) => _engine!.MoveTo(Session!.Canvas, input.Position);
}

/// <summary>
/// Square eraser. Left: secondary color on an opaque background layer, transparency elsewhere.
/// Right: replaces only the primary color with the secondary color.
/// </summary>
public sealed class EraserTool(IToolHost host) : StrokeToolBase(host)
{
    private (int X, int Y) _last;
    private uint _stamp;
    private int _size;

    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Eraser;

    /// <inheritdoc/>
    protected override string StepName => "Eraser";

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint) => ToolCursor.EraserSquare;

    /// <inheritdoc/>
    protected override StrokeSession BeginStroke(PointerInput input)
    {
        var doc = Host.Document;
        var st = Host.Settings;
        StrokeSession s;
        if (input.Button == PointerButton.Right)
        {
            s = new StrokeSession(doc, StrokeMode.ColorReplace, 1, st.Primary, st.Secondary);
            _stamp = ColorUtil.White;
        }
        else if (doc.ActiveLayer.ErasesToTransparent)
        {
            s = new StrokeSession(doc, StrokeMode.EraseTransparent, 1);
            _stamp = ColorUtil.White;
        }
        else
        {
            s = new StrokeSession(doc, StrokeMode.Paint, 1);
            _stamp = ColorUtil.FromColor(st.Secondary);
        }

        _size = st.EraserSize;
        _last = input.Pixel;
        Raster.Square(s.Canvas, input.Position, _size, _stamp);
        return s;
    }

    /// <inheritdoc/>
    protected override void ContinueStroke(PointerInput input)
    {
        var p = input.Pixel;
        var c = Session!.Canvas;
        Raster.Bresenham(_last.X, _last.Y, p.X, p.Y, (x, y) => Raster.Square(c, new Point(x + 0.5, y + 0.5), _size, _stamp));
        _last = p;
    }
}

/// <summary>Flood fill bucket (exact match, 4-connected) reading the active layer's composite.</summary>
public sealed class FillTool(IToolHost host) : ToolBase(host)
{
    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Fill;

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint) => ToolCursor.Bucket;

    /// <inheritdoc/>
    public override void OnPointerDown(PointerInput input)
    {
        if (input.Button is not (PointerButton.Left or PointerButton.Right))
        {
            return;
        }

        var doc = Host.Document;
        var (x, y) = input.Pixel;
        if (!doc.Bounds.Contains(x, y))
        {
            return;
        }

        Apply(doc, x, y, ColorFor(input.Button));
    }

    /// <summary>Fills the region at (x, y) of the active layer with a color and commits one undo step.</summary>
    public static bool Apply(Document.PaintDocument doc, int x, int y, Color color)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var layer = doc.ActiveLayer;
        var composite = doc.LayerComposite(layer);
        var fill = ColorUtil.FromColor(color);
        if (composite[x, y] == fill)
        {
            return false;
        }

        var (spans, bounds) = FloodFill.Region(composite.Pixels, composite.Width, composite.Height, x, y);
        var seg = layer.TopSegment.Pixels;
        foreach (var s in spans)
        {
            for (var px = s.X0; px <= s.X1;)
            {
                var tx = px >> Tile.Shift;
                var end = Math.Min(s.X1, ((tx + 1) << Tile.Shift) - 1);
                var data = seg.GetWritableData(tx, s.Y >> Tile.Shift);
                data.AsSpan(((s.Y & (Tile.Size - 1)) << Tile.Shift) + (px & (Tile.Size - 1)), end - px + 1).Fill(fill);
                px = end + 1;
            }
        }

        doc.Invalidate(layer, bounds);
        doc.Commit("Fill");
        return true;
    }
}

/// <summary>Color picker: samples the visible composite, then returns to the previous tool.</summary>
public sealed class PickerTool(IToolHost host) : ToolBase(host)
{
    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Picker;

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint) => ToolCursor.Dropper;

    /// <inheritdoc/>
    public override void OnPointerDown(PointerInput input)
    {
        if (input.Button is not (PointerButton.Left or PointerButton.Right))
        {
            return;
        }

        var (x, y) = input.Pixel;
        if (!Host.Document.Bounds.Contains(x, y))
        {
            return;
        }

        Host.SetColor(input.Button == PointerButton.Left, ColorUtil.ToColor(Host.Document.CompositePixel(x, y)));
        Host.RestorePreviousTool();
    }
}

/// <summary>Magnifier: left click zooms in, right click zooms out, centered on the cursor.</summary>
public sealed class MagnifierTool(IToolHost host) : ToolBase(host)
{
    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Magnifier;

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint) => ToolCursor.Magnifier;

    /// <inheritdoc/>
    public override void OnPointerDown(PointerInput input)
    {
        if (input.Button is PointerButton.Left or PointerButton.Right)
        {
            Host.ZoomStep(input.Position, input.Button == PointerButton.Left);
        }
    }
}
