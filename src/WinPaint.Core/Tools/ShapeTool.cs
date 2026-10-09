using System.Windows;
using System.Windows.Input;
using WinPaint.Core.Document;
using WinPaint.Core.Shapes;

namespace WinPaint.Core.Tools;

/// <summary>
/// Draws the 23 shapes. After drawing, a shape stays floating with a bounding box and handles: it can be moved,
/// resized and restyled live until it is committed (click outside, Enter/Esc, tool switch). Committed shapes are
/// plain pixels.
/// </summary>
public sealed class ShapeTool(IToolHost host) : ToolBase(host), IOverlayTool
{
    private Phase _phase;
    private ShapeKind _kind;
    private bool _swap;
    private Point _start;
    private Point _end;
    private readonly List<Point> _points = [];
    private List<Point> _origPoints = [];
    private Rect _origBounds;
    private Rect _bounds;
    private Rect _boundsAtDrag;
    private Point _dragStart;
    private HandleKind _handle;
    private bool _bending;
    private int _seed;

    private enum Phase
    {
        Idle,
        Dragging,
        CurveBend1,
        CurveBend2,
        PolygonBuilding,
        Adjusting,
        AdjustDrag,
    }

    /// <inheritdoc/>
    public override ToolKind Kind => ToolKind.Shape;

    /// <inheritdoc/>
    public override bool HasPendingOperation => _phase != Phase.Idle;

    /// <summary>True while a drawn shape is floating with handles.</summary>
    public bool IsAdjusting => _phase is Phase.Adjusting or Phase.AdjustDrag;

    /// <summary>Current bounds of the floating shape.</summary>
    public Rect AdjustBounds => _bounds;

    /// <inheritdoc/>
    public ToolOverlay? Overlay => _phase switch
    {
        Phase.Adjusting or Phase.AdjustDrag => new ToolOverlay { HandleBox = _bounds },
        Phase.CurveBend1 or Phase.CurveBend2 => new ToolOverlay { ControlPoints = [_points[0], _points[1]] },
        Phase.PolygonBuilding => new ToolOverlay { ControlPoints = [_points[0]] },
        _ => null,
    };

    /// <inheritdoc/>
    public override ToolCursor CursorAt(Point canvasPoint)
    {
        if (IsAdjusting)
        {
            return HandleBox.HitTest(_bounds, canvasPoint, Host.HandleTolerance) switch
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

    /// <inheritdoc/>
    public override void OnPointerDown(PointerInput input)
    {
        if (input.Button is not (PointerButton.Left or PointerButton.Right))
        {
            return;
        }

        var p = input.Position;
        switch (_phase)
        {
            case Phase.Adjusting:
                var h = HandleBox.HitTest(_bounds, p, Host.HandleTolerance);
                if (h != HandleKind.None && input.Button == PointerButton.Left)
                {
                    _handle = h;
                    _dragStart = p;
                    _boundsAtDrag = _bounds;
                    _phase = Phase.AdjustDrag;
                    return;
                }

                CommitPending();
                Begin(input);
                return;
            case Phase.CurveBend1:
            case Phase.CurveBend2:
                _bending = true;
                SetBend(p);
                return;
            case Phase.PolygonBuilding:
                if (input.ClickCount >= 2 || ((p - _points[0]).Length <= Math.Max(4, Host.HandleTolerance * 1.5) && _points.Count >= 3))
                {
                    if (input.ClickCount >= 2 && _points.Count > 3)
                    {
                        _points.RemoveAt(_points.Count - 1);
                    }

                    EnterAdjusting();
                    return;
                }

                _points.Add(p);
                Render();
                return;
            case Phase.Idle:
                Begin(input);
                return;
        }
    }

    /// <inheritdoc/>
    public override void OnPointerMove(PointerInput input)
    {
        if (input.Button == PointerButton.None)
        {
            return;
        }

        var p = input.Position;
        switch (_phase)
        {
            case Phase.Dragging:
                _end = p;
                if (_kind == ShapeKind.Polygon)
                {
                    _points[^1] = input.Shift ? ShapeGeometry.ConstrainLine(_points[^2], p) : p;
                }
                else if (_kind is ShapeKind.Line or ShapeKind.Curve && input.Shift)
                {
                    _end = ShapeGeometry.ConstrainLine(_start, p);
                }

                Render(input.Shift);
                break;
            case Phase.CurveBend1:
            case Phase.CurveBend2:
                if (_bending)
                {
                    SetBend(p);
                }

                break;
            case Phase.PolygonBuilding:
                _points[^1] = input.Shift ? ShapeGeometry.ConstrainLine(_points[^2], p) : p;
                Render();
                break;
            case Phase.AdjustDrag:
                var nb = HandleBox.Resize(_boundsAtDrag, _handle, p - _dragStart, input.Shift, 1);
                _bounds = new Rect(Math.Round(nb.X), Math.Round(nb.Y), Math.Max(1, Math.Round(nb.Width)), Math.Max(1, Math.Round(nb.Height)));
                Render();
                break;
        }
    }

    /// <inheritdoc/>
    public override void OnPointerUp(PointerInput input)
    {
        switch (_phase)
        {
            case Phase.Dragging:
                if ((_end - _start).Length < 1 && _kind != ShapeKind.Polygon)
                {
                    Cancel();
                    return;
                }

                if (_kind == ShapeKind.Curve)
                {
                    _points.Clear();
                    _points.Add(_start);
                    _points.Add(_end);
                    _phase = Phase.CurveBend1;
                }
                else if (_kind == ShapeKind.Polygon)
                {
                    _points.Add(_points[^1]);
                    _phase = Phase.PolygonBuilding;
                }
                else
                {
                    Render(input.Shift);
                    if (_kind == ShapeKind.Line)
                    {
                        _points.Clear();
                        _points.Add(_start);
                        _points.Add(_end);
                    }
                    else
                    {
                        _bounds = BoxFromDrag(input.Shift);
                    }

                    EnterAdjusting();
                }

                Host.ToolStateChanged();
                break;
            case Phase.CurveBend1:
                _bending = false;
                _phase = Phase.CurveBend2;
                break;
            case Phase.CurveBend2:
                _bending = false;
                EnterAdjusting();
                break;
            case Phase.AdjustDrag:
                _phase = Phase.Adjusting;
                break;
        }
    }

    /// <inheritdoc/>
    public override bool OnKey(Key key, ModifierKeys modifiers)
    {
        if (key is Key.Escape or Key.Enter && _phase != Phase.Idle)
        {
            if (_phase == Phase.Dragging)
            {
                Cancel();
            }
            else
            {
                CommitPending();
            }

            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override void CommitPending()
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        if (_phase == Phase.PolygonBuilding && _points.Count >= 2)
        {
            if (_points.Count > 2 && (_points[^1] - _points[^2]).Length < 1)
            {
                _points.RemoveAt(_points.Count - 1);
            }

            EnterAdjusting();
        }

        var doc = Host.Document;
        if (doc.Floating is { } f)
        {
            SelectionOperations.Stamp(doc, doc.ActiveLayer, f.Pixels, f.X, f.Y);
            doc.Floating = null;
            doc.Commit("Shape");
        }

        _phase = Phase.Idle;
        _points.Clear();
        Host.ToolStateChanged();
    }

    /// <summary>Re-renders the floating shape after outline/fill/size/color/opacity changes.</summary>
    public void RefreshFromSettings()
    {
        if (_phase != Phase.Idle)
        {
            Render();
        }
    }

    /// <summary>The spec of the shape being drawn/adjusted (null when idle).</summary>
    public ShapeSpec? CurrentSpec(bool constrain = false)
    {
        if (_phase == Phase.Idle)
        {
            return null;
        }

        var s = Host.Settings;
        var outlineColor = _swap ? s.Secondary : s.Primary;
        var fillColor = _swap ? s.Primary : s.Secondary;
        var spec = new ShapeSpec
        {
            Kind = _kind,
            Outline = s.Outline,
            Fill = s.Fill,
            Size = s.ShapeSize,
            OutlineColor = outlineColor,
            FillColor = fillColor,
            Opacity = s.Opacity,
            Seed = _seed,
        };
        if (IsAdjusting)
        {
            return _kind is ShapeKind.Line or ShapeKind.Curve or ShapeKind.Polygon
                ? spec with { Points = MapPoints(), Closed = true }
                : spec with { Bounds = _bounds };
        }

        return _kind switch
        {
            ShapeKind.Line => spec with { Points = [_start, _end] },
            ShapeKind.Curve => spec with { Points = _points.Count >= 2 ? [.. _points] : [_start, _end] },
            ShapeKind.Polygon => spec with { Points = [.. _points], Closed = false },
            _ => spec with { Bounds = BoxFromDrag(constrain) },
        };
    }

    private void Begin(PointerInput input)
    {
        _kind = Host.Settings.Shape;
        _swap = input.Button == PointerButton.Right;
        _seed = Host.NextSeed();
        _start = _end = input.Position;
        _points.Clear();
        if (_kind == ShapeKind.Polygon)
        {
            _points.Add(input.Position);
            _points.Add(input.Position);
        }

        _phase = Phase.Dragging;
    }

    private void SetBend(Point p)
    {
        if (_phase == Phase.CurveBend1)
        {
            while (_points.Count > 2)
            {
                _points.RemoveAt(_points.Count - 1);
            }

            _points.Add(p);
        }
        else
        {
            while (_points.Count > 3)
            {
                _points.RemoveAt(_points.Count - 1);
            }

            _points.Add(p);
        }

        Render();
    }

    private Rect BoxFromDrag(bool constrain)
    {
        var r = ShapeGeometry.DragBounds(_start, _end, constrain);
        return new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Max(1, Math.Round(r.Width)), Math.Max(1, Math.Round(r.Height)));
    }

    private void EnterAdjusting()
    {
        if (_kind is ShapeKind.Line or ShapeKind.Curve or ShapeKind.Polygon)
        {
            _origPoints = [.. _points];
            var minX = _points.Min(p => p.X);
            var minY = _points.Min(p => p.Y);
            _origBounds = new Rect(minX, minY, _points.Max(p => p.X) - minX, _points.Max(p => p.Y) - minY);
            _bounds = new Rect(Math.Floor(_origBounds.X), Math.Floor(_origBounds.Y), Math.Max(1, Math.Ceiling(_origBounds.Width)), Math.Max(1, Math.Ceiling(_origBounds.Height)));
            _origBounds = _bounds;
        }

        _phase = Phase.Adjusting;
        Render();
    }

    private List<Point> MapPoints()
    {
        var sx = _origBounds.Width > 0 ? _bounds.Width / _origBounds.Width : 1;
        var sy = _origBounds.Height > 0 ? _bounds.Height / _origBounds.Height : 1;
        return _origPoints.Select(p => new Point(_bounds.X + ((p.X - _origBounds.X) * sx), _bounds.Y + ((p.Y - _origBounds.Y) * sy))).ToList();
    }

    private void Render(bool constrain = false)
    {
        var spec = CurrentSpec(constrain);
        var doc = Host.Document;
        var r = spec is null ? null : ShapeRenderer.Render(spec, doc.Width, doc.Height);
        doc.Floating = r is { } x ? new FloatingImage(x.Pixels, x.X, x.Y) : null;
        Host.ToolStateChanged();
    }

    private void Cancel()
    {
        Host.Document.Floating = null;
        _phase = Phase.Idle;
        _points.Clear();
        Host.ToolStateChanged();
    }
}
