using System.Windows;
using System.Windows.Input;
using WinPaint.Core.Document;
using WinPaint.Core.Tools;

namespace WinPaint.App.Controls;

/// <summary>Pointer input: tool routing, panning, canvas resize handles and text box dragging.</summary>
public sealed partial class CanvasView
{
    /// <summary>Half-size of canvas resize handles in DIPs.</summary>
    internal const double ResizeHandleHalf = 4;

    private DragMode _drag;
    private PointerButton _dragButton;
    private Point _dragStartView;
    private Point _panStartScroll;
    private Point _pointerView;
    private HandleKind _resizeHandle;
    private Size? _resizePreview;

    private enum DragMode
    {
        None,
        Tool,
        Pan,
        CanvasResize,
        TextFrame,
    }

    /// <summary>Live canvas-resize preview size (canvas px).</summary>
    internal Size? ResizePreview => _resizePreview;

    /// <summary>Text object under the pointer when the Select/Text tool hovers it.</summary>
    internal TextObject? HoverText { get; private set; }

    /// <summary>Last pointer position over the viewport (DIPs).</summary>
    internal Point PointerView => _pointerView;

    /// <summary>Canvas resize handle rectangles in view coordinates.</summary>
    internal IEnumerable<(HandleKind Kind, Rect Rect)> CanvasResizeHandles()
    {
        if (_doc is null)
        {
            yield break;
        }

        var tl = _vt.CanvasToView(new Point(0, 0));
        var br = _vt.CanvasToView(new Point(_doc.Width, _doc.Height));
        var h = ResizeHandleHalf;
        Rect At(double x, double y) => new(x - h, y - h, 2 * h, 2 * h);
        yield return (HandleKind.Right, At(br.X + h + 1, (tl.Y + br.Y) / 2));
        yield return (HandleKind.Bottom, At((tl.X + br.X) / 2, br.Y + h + 1));
        yield return (HandleKind.BottomRight, At(br.X + h + 1, br.Y + h + 1));
    }

    private void HookInput()
    {
        _viewport.MouseDown += OnMouseDown;
        _viewport.MouseMove += OnMouseMove;
        _viewport.MouseUp += OnMouseUp;
        _viewport.MouseWheel += OnMouseWheel;
        _viewport.MouseLeave += (_, _) =>
        {
            if (_drag == DragMode.None)
            {
                PointerCanvas = null;
                Controller?.ReportCursor(null);
                HoverText = null;
                _hRuler.InvalidateVisual();
                _vRuler.InvalidateVisual();
                _overlay.InvalidateVisual();
            }
        };
        _viewport.LostMouseCapture += (_, _) =>
        {
            if (_drag != DragMode.None && Mouse.Captured != _viewport)
            {
                EndDrag(_pointerView, _dragButton);
            }
        };
    }

    private static PointerButton Map(MouseButton b) => b switch
    {
        MouseButton.Left => PointerButton.Left,
        MouseButton.Right => PointerButton.Right,
        MouseButton.Middle => PointerButton.Middle,
        _ => PointerButton.None,
    };

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HandleDown(e.GetPosition(_viewport), Map(e.ChangedButton), Keyboard.Modifiers, e.ClickCount, Keyboard.IsKeyDown(Key.Space)))
        {
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => HandleMove(e.GetPosition(_viewport), Keyboard.Modifiers);

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (HandleUp(e.GetPosition(_viewport), Map(e.ChangedButton), Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    /// <summary>Pointer pressed at a view position. Shared by real mouse input and the automation bridge.</summary>
    internal bool HandleDown(Point pos, PointerButton button, ModifierKeys mods, int clickCount, bool spaceDown)
    {
        if (Controller is null || _doc is null || _drag != DragMode.None)
        {
            if (_drag == DragMode.Tool && Controller is not null)
            {
                // Second button during a stroke: let the tool decide (cancels the stroke).
                Controller.PointerDown(new PointerInput(_vt.ViewToCanvas(pos), button, mods, clickCount));
                return true;
            }

            return false;
        }

        _pointerView = pos;
        _dragStartView = pos;
        if (button == PointerButton.None)
        {
            return false;
        }

        if (!_editor.IsKeyboardFocusWithin)
        {
            _viewport.Focus();
        }

        if (button == PointerButton.Middle || (button == PointerButton.Left && spaceDown))
        {
            BeginDrag(DragMode.Pan, button);
            _panStartScroll = new Point(_vt.ScrollX, _vt.ScrollY);
            _viewport.Cursor = Cursors.ScrollAll;
            return true;
        }

        if (Controller.TextSession is not null)
        {
            if (clickCount >= 2 && button == PointerButton.Left && Controller.EditorDoubleClick(_vt.ViewToCanvas(pos)))
            {
                return true;
            }

            var frame = _editor.HitTestFrame(pos);
            if (frame != HandleKind.None && button == PointerButton.Left)
            {
                _editor.BeginFrameDrag(frame, pos);
                BeginDrag(DragMode.TextFrame, button);
                return true;
            }

            Controller.CommitTextEdit();
            return true;
        }

        if (button == PointerButton.Left)
        {
            foreach (var (kind, rect) in CanvasResizeHandles())
            {
                if (rect.Contains(pos))
                {
                    _resizeHandle = kind;
                    BeginDrag(DragMode.CanvasResize, button);
                    return true;
                }
            }
        }

        BeginDrag(DragMode.Tool, button);
        Controller.PointerDown(new PointerInput(_vt.ViewToCanvas(pos), button, mods, clickCount));
        _toolTimer.Start();
        return true;
    }

    private void BeginDrag(DragMode mode, PointerButton button)
    {
        _drag = mode;
        _dragButton = button;
        _viewport.CaptureMouse();
    }

    /// <summary>Pointer moved to a view position.</summary>
    internal void HandleMove(Point pos, ModifierKeys mods)
    {
        if (Controller is null || _doc is null)
        {
            return;
        }

        _pointerView = pos;
        var canvas = _vt.ViewToCanvas(pos);
        var inside = canvas.X >= 0 && canvas.Y >= 0 && canvas.X < _doc.Width && canvas.Y < _doc.Height;
        PointerCanvas = inside || _drag != DragMode.None ? canvas : null;
        Controller.ReportCursor(inside ? canvas : null);
        if (ShowRulers)
        {
            _hRuler.InvalidateVisual();
            _vRuler.InvalidateVisual();
        }

        switch (_drag)
        {
            case DragMode.Pan:
                ScrollTo(_panStartScroll.X - (pos.X - _dragStartView.X), _panStartScroll.Y - (pos.Y - _dragStartView.Y));
                return;
            case DragMode.CanvasResize:
                UpdateResizePreview(canvas);
                return;
            case DragMode.TextFrame:
                _editor.UpdateFrameDrag(pos, mods);
                return;
            case DragMode.Tool:
                Controller.PointerMove(new PointerInput(canvas, _dragButton, mods));
                return;
        }

        Controller.PointerMove(new PointerInput(canvas, PointerButton.None, mods));
        UpdateHover(canvas);
        UpdateCursor();
        if (CursorNeedsOverlay)
        {
            _overlay.InvalidateVisual();
        }
    }

    /// <summary>Pointer released at a view position.</summary>
    internal bool HandleUp(Point pos, PointerButton button, ModifierKeys mods)
    {
        if (_drag == DragMode.None || button != _dragButton)
        {
            return false;
        }

        EndDrag(pos, button, mods);
        return true;
    }

    private void EndDrag(Point pos, PointerButton button, ModifierKeys mods = ModifierKeys.None)
    {
        var mode = _drag;
        _drag = DragMode.None;
        _toolTimer.Stop();
        if (_viewport.IsMouseCaptured)
        {
            _viewport.ReleaseMouseCapture();
        }

        var canvas = _vt.ViewToCanvas(pos);
        switch (mode)
        {
            case DragMode.Pan:
                UpdateCursor();
                break;
            case DragMode.CanvasResize:
                if (_resizePreview is { } size)
                {
                    Controller?.ResizeCanvas((int)size.Width, (int)size.Height);
                }

                _resizePreview = null;
                Controller?.ReportResizePreview(null);
                _overlay.InvalidateVisual();
                break;
            case DragMode.TextFrame:
                _editor.EndFrameDrag();
                break;
            case DragMode.Tool:
                Controller?.PointerUp(new PointerInput(canvas, button, mods));
                break;
        }
    }
    private void UpdateResizePreview(Point canvas)
    {
        if (_doc is null)
        {
            return;
        }

        var w = _resizeHandle is HandleKind.Right or HandleKind.BottomRight ? Math.Max(1, (int)Math.Round(canvas.X)) : _doc.Width;
        var h = _resizeHandle is HandleKind.Bottom or HandleKind.BottomRight ? Math.Max(1, (int)Math.Round(canvas.Y)) : _doc.Height;
        w = Math.Min(w, 100000);
        h = Math.Min(h, 100000);
        _resizePreview = new Size(w, h);
        Controller?.ReportResizePreview(_resizePreview);
        _overlay.InvalidateVisual();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var pos = e.GetPosition(_viewport);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var z = e.Delta > 0 ? Core.View.ViewTransform.NextPreset(_vt.Zoom) : Core.View.ViewTransform.PreviousPreset(_vt.Zoom);
            SetZoom(z, pos);
            return;
        }

        var amount = -e.Delta / 120.0 * 48;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            ScrollBy(amount, 0);
        }
        else
        {
            ScrollBy(0, amount);
        }
    }

    /// <summary>Horizontal wheel / touchpad scroll (WM_MOUSEHWHEEL) forwarded by the window.</summary>
    public void OnHorizontalWheel(int delta) => ScrollBy(delta / 120.0 * 48, 0);

    private void UpdateHover(Point canvas)
    {
        TextObject? hover = null;
        if (_doc is not null && Controller is not null && Controller.TextSession is null
            && Controller.ActiveToolKind is ToolKind.RectSelect or ToolKind.FreeSelect or ToolKind.Text)
        {
            hover = _doc.HitTestText(canvas)?.Text;
        }

        if (!ReferenceEquals(hover, HoverText))
        {
            HoverText = hover;
            _overlay.InvalidateVisual();
        }
    }

    /// <summary>True when the brush outline is too big for a cursor and must be drawn by the overlay.</summary>
    internal bool CursorNeedsOverlay =>
        Controller is not null && _drag is DragMode.None or DragMode.Tool && Controller.ActiveToolKind is ToolKind.Brush or ToolKind.Eraser
        && Controller.CursorSize * _vt.Zoom > CursorFactory.MaxOutlineCursor;

    /// <summary>Updates the mouse cursor for the current pointer position.</summary>
    internal void UpdateCursor()
    {
        if (Controller is null || _doc is null || _drag == DragMode.Pan)
        {
            return;
        }

        if (Keyboard.IsKeyDown(Key.Space))
        {
            _viewport.Cursor = Cursors.Hand;
            return;
        }

        if (Controller.TextSession is not null)
        {
            _viewport.Cursor = TextEditorOverlay.CursorFor(_editor.HitTestFrame(_pointerView));
            return;
        }

        foreach (var (kind, rect) in CanvasResizeHandles())
        {
            if (rect.Contains(_pointerView))
            {
                _viewport.Cursor = kind switch
                {
                    HandleKind.Right => Cursors.SizeWE,
                    HandleKind.Bottom => Cursors.SizeNS,
                    _ => Cursors.SizeNWSE,
                };
                return;
            }
        }

        if (HoverText is not null)
        {
            _viewport.Cursor = Cursors.IBeam;
            return;
        }

        var kindAt = Controller.CursorAt(_vt.ViewToCanvas(_pointerView));
        var outline = Controller.CursorSize * _vt.Zoom;
        _viewport.Cursor = CursorFactory.Get(kindAt, outline, _vt.DpiScale);
    }
}
