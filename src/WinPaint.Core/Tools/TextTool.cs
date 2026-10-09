using System.Windows;
using WinPaint.Core.Text;

namespace WinPaint.Core.Tools;

/// <summary>
/// Text tool: click (default box) or drag (custom box) to create a text box. Double-click on live text reopens it.
/// The first click of a double-click never leaves a stray object: a new box is created only on mouse-up and a
/// blank new box is discarded without an undo entry.
/// </summary>
public sealed class TextTool(IToolHost host) : ToolBase(host), IOverlayTool
{
    /// <summary>Default width of a click-created text box (canvas px).</summary>
    public const double DefaultBoxWidth = 240;

    private Point? _dragStart;
    private Point _dragNow;

    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Text;

    /// <inheritdoc/>
    public ToolOverlay? Overlay => _dragStart is { } s && (_dragNow - s).Length > 2
        ? new ToolOverlay { DashedRect = new Rect(s, _dragNow) }
        : null;

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint) => ToolCursor.IBeam;

    /// <inheritdoc/>
    public override void OnPointerDown(PointerInput input)
    {
        if (input.Button != PointerButton.Left)
        {
            return;
        }

        if (input.ClickCount >= 2)
        {
            _dragStart = null;
            Host.TryBeginTextEditAt(input.Position);
            return;
        }

        if (Host.IsEditingText)
        {
            Host.CommitTextEdit();
            return;
        }

        var d = Host.Document;
        var p = input.Position;
        if (p.X < 0 || p.Y < 0 || p.X >= d.Width || p.Y >= d.Height)
        {
            return;
        }

        _dragStart = new Point(Math.Floor(p.X), Math.Floor(p.Y));
        _dragNow = _dragStart.Value;
    }

    /// <inheritdoc/>
    public override void OnPointerMove(PointerInput input)
    {
        if (_dragStart is null || input.Button != PointerButton.Left)
        {
            return;
        }

        var d = Host.Document;
        _dragNow = new Point(Math.Clamp(Math.Round(input.Position.X), 0, d.Width), Math.Clamp(Math.Round(input.Position.Y), 0, d.Height));
        Host.ToolStateChanged();
    }

    /// <inheritdoc/>
    public override void OnPointerUp(PointerInput input)
    {
        if (_dragStart is not { } s || input.Button != PointerButton.Left)
        {
            return;
        }

        _dragStart = null;
        var d = Host.Document;
        var end = _dragNow;
        Rect box;
        if (Math.Abs(end.X - s.X) < 8 && Math.Abs(end.Y - s.Y) < 8)
        {
            var probe = new Document.TextObject { FontFamily = Host.Settings.FontFamily, FontSizePt = Host.Settings.FontSizePt, Bold = Host.Settings.Bold, Italic = Host.Settings.Italic };
            var line = TextLayoutEngine.LineHeight(probe);
            var w = Math.Max(Math.Min(DefaultBoxWidth, d.Width - s.X), 24);
            box = new Rect(s.X, s.Y, w, Math.Ceiling(line));
        }
        else
        {
            box = new Rect(s, end);
            box = new Rect(box.X, box.Y, Math.Max(8, box.Width), Math.Max(8, box.Height));
        }

        Host.ToolStateChanged();
        Host.BeginNewText(box);
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _dragStart = null;
        if (Host.IsEditingText)
        {
            Host.CommitTextEdit();
        }
    }
}
