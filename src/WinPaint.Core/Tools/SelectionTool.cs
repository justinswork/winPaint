using System.Windows;
using System.Windows.Input;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Tools;

/// <summary>
/// Rectangular and free-form selection with Paint's floating-selection behaviors: move, resize by handles,
/// Ctrl+drag duplicate, Shift+drag stamp trail, transparent selection, nudge, and commit on Esc/click outside.
/// Lifting pixels flattens intersecting live text first (one undo step restores it).
/// </summary>
public sealed partial class SelectionTool(IToolHost host, bool freeForm) : ToolBase(host), IOverlayTool
{
    private SelectionRegion? _region;
    private PixelBuffer? _sourceOriginal;
    private PixelBuffer? _source;
    private Rect _bounds;
    private Layer? _layer;
    private string _stepName = "Move selection";
    private DragKind _drag;
    private Point _dragStart;
    private Rect _boundsAtDragStart;
    private HandleKind _handle;
    private Point _selectNow;
    private readonly List<Point> _lasso = [];

    private enum DragKind
    {
        None,
        Select,
        Move,
        Resize,
    }

    /// <inheritdoc/>
    public override ToolKind Kind => freeForm ? ToolKind.FreeSelect : ToolKind.RectSelect;

    /// <summary>True when something is selected (lifted or not).</summary>
    public bool HasSelection => _region is not null || IsFloating;

    /// <summary>True when the selected pixels are lifted and floating.</summary>
    public bool IsFloating => _source is not null;

    /// <inheritdoc/>
    public override bool HasPendingOperation => HasSelection || _drag != DragKind.None;

    /// <summary>Bounds of the selection (canvas px).</summary>
    public PixelRect SelectionBounds => IsFloating ? PixelRect.FromRect(_bounds) : _region?.Bounds ?? PixelRect.Empty;

    /// <summary>The current selection region (null while floating or nothing selected).</summary>
    public SelectionRegion? Region => _region;

    /// <inheritdoc/>
    public ToolOverlay? Overlay
    {
        get
        {
            if (_drag == DragKind.Select)
            {
                return freeForm
                    ? new ToolOverlay { AntsOpen = [.. _lasso] }
                    : new ToolOverlay { DashedRect = SelectRect(_dragStart, _selectNow, false).ToRect() };
            }

            if (IsFloating)
            {
                return new ToolOverlay { HandleBox = _bounds };
            }

            if (_region is not null)
            {
                return _region.IsFreeForm
                    ? new ToolOverlay { Ants = _region.Outlines }
                    : new ToolOverlay { HandleBox = _region.Bounds.ToRect() };
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint)
    {
        if (HasSelection)
        {
            var h = HandleBox.HitTest(CurrentRect, canvasPoint, Host.HandleTolerance);
            return h switch
            {
                HandleKind.Move => ToolCursor.Move,
                HandleKind.TopLeft or HandleKind.BottomRight => ToolCursor.SizeNwse,
                HandleKind.TopRight or HandleKind.BottomLeft => ToolCursor.SizeNesw,
                HandleKind.Left or HandleKind.Right => ToolCursor.SizeWe,
                HandleKind.Top or HandleKind.Bottom => ToolCursor.SizeNs,
                _ => ToolCursor.Crosshair,
            };
        }

        return ToolCursor.Crosshair;
    }

    private Rect CurrentRect => IsFloating ? _bounds : _region?.Bounds.ToRect() ?? Rect.Empty;

    /// <inheritdoc/>
    public override void OnPointerDown(PointerInput input)
    {
        if (input.Button is not (PointerButton.Left or PointerButton.Right))
        {
            return;
        }

        if (input.ClickCount >= 2 && input.Button == PointerButton.Left && Host.TryBeginTextEditAt(input.Position))
        {
            _drag = DragKind.None;
            return;
        }

        var p = input.Position;
        if (HasSelection)
        {
            var h = HandleBox.HitTest(CurrentRect, p, Host.HandleTolerance);
            if (input.Button == PointerButton.Right && h == HandleKind.Move)
            {
                Host.ShowContextMenu();
                return;
            }

            if (h == HandleKind.Move && input.Button == PointerButton.Left)
            {
                if (!IsFloating)
                {
                    Lift(clearSource: !input.Ctrl, "Move selection");
                }
                else if (input.Ctrl)
                {
                    StampFloating();
                }

                StartDrag(DragKind.Move, p);
                return;
            }

            if (h is not HandleKind.None and not HandleKind.Move && input.Button == PointerButton.Left)
            {
                if (!IsFloating)
                {
                    Lift(clearSource: true, "Resize selection");
                }

                _handle = h;
                StartDrag(DragKind.Resize, p);
                return;
            }

            CommitPending();
        }

        StartDrag(DragKind.Select, ClampToCanvas(p));
        _selectNow = _dragStart;
        _lasso.Clear();
        _lasso.Add(_dragStart);
    }

    /// <inheritdoc/>
    public override void OnPointerMove(PointerInput input)
    {
        if (input.Button == PointerButton.None)
        {
            return;
        }

        var p = input.Position;
        switch (_drag)
        {
            case DragKind.Select:
                _selectNow = ClampToCanvas(p);
                if (freeForm && (_lasso.Count == 0 || (_lasso[^1] - _selectNow).Length >= 1))
                {
                    _lasso.Add(_selectNow);
                }

                if (!freeForm && input.Shift)
                {
                    var r = SelectRect(_dragStart, _selectNow, true);
                    _selectNow = new Point(_dragStart.X < _selectNow.X ? r.Right : r.X, _dragStart.Y < _selectNow.Y ? r.Bottom : r.Y);
                }

                break;
            case DragKind.Move:
                var d = p - _dragStart;
                _bounds = new Rect(Math.Round(_boundsAtDragStart.X + d.X), Math.Round(_boundsAtDragStart.Y + d.Y), _bounds.Width, _bounds.Height);
                UpdateFloating();
                if (input.Shift)
                {
                    StampFloating();
                }

                break;
            case DragKind.Resize:
                var nb = HandleBox.Resize(_boundsAtDragStart, _handle, p - _dragStart, input.Shift, 1);
                _bounds = new Rect(Math.Round(nb.X), Math.Round(nb.Y), Math.Max(1, Math.Round(nb.Width)), Math.Max(1, Math.Round(nb.Height)));
                UpdateFloating();
                break;
        }
    }

    /// <inheritdoc/>
    public override void OnPointerUp(PointerInput input)
    {
        var kind = _drag;
        _drag = DragKind.None;
        if (kind != DragKind.Select)
        {
            return;
        }

        if (freeForm)
        {
            _region = _lasso.Count >= 3 ? SelectionRegion.FromPolygon(_lasso, Host.Document.Bounds) : null;
        }
        else
        {
            var r = SelectRect(_dragStart, _selectNow, input.Shift).Intersect(Host.Document.Bounds);
            _region = r.Width >= 1 && r.Height >= 1 && (_selectNow - _dragStart).Length >= 1 ? SelectionRegion.FromRect(r) : null;
        }

        _lasso.Clear();
    }

    /// <inheritdoc/>
    public override bool OnKey(Key key, ModifierKeys modifiers)
    {
        var step = (modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (key)
        {
            case Key.Escape:
            case Key.Enter:
                if (HasPendingOperation)
                {
                    CommitPending();
                    return true;
                }

                return false;
            case Key.Left:
                return Nudge(-step, 0);
            case Key.Right:
                return Nudge(step, 0);
            case Key.Up:
                return Nudge(0, -step);
            case Key.Down:
                return Nudge(0, step);
        }

        return false;
    }

    /// <inheritdoc/>
    public override void CommitPending()
    {
        _drag = DragKind.None;
        if (IsFloating)
        {
            CommitFloating();
        }

        _region = null;
    }

    /// <summary>Moves the selection by a pixel offset (lifting it first). Returns false when nothing is selected.</summary>
    public bool Nudge(int dx, int dy)
    {
        if (!HasSelection)
        {
            return false;
        }

        if (!IsFloating)
        {
            Lift(clearSource: true, "Move selection");
        }

        _bounds.Offset(dx, dy);
        UpdateFloating();
        return true;
    }

    private void StartDrag(DragKind kind, Point p)
    {
        _drag = kind;
        _dragStart = p;
        _boundsAtDragStart = _bounds;
    }

    private Point ClampToCanvas(Point p) =>
        new(Math.Clamp(Math.Round(p.X), 0, Host.Document.Width), Math.Clamp(Math.Round(p.Y), 0, Host.Document.Height));

    private static PixelRect SelectRect(Point a, Point b, bool square)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (square)
        {
            var m = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = Math.Sign(dx == 0 ? 1 : dx) * m;
            dy = Math.Sign(dy == 0 ? 1 : dy) * m;
        }

        return PixelRect.FromRect(new Rect(a, new Point(a.X + dx, a.Y + dy)));
    }
}
