using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.Text;
using WinPaint.Core.Tools;

namespace WinPaint.App.Controls;

/// <summary>
/// The in-place text editor: a WPF TextBox with transparent glyphs (only caret and selection are visible) laid
/// out with the same font metrics and wrap width as the document renderer and transformed exactly like the text
/// object. The visible glyphs are drawn live by the real renderer, so edit mode matches committed mode.
/// </summary>
internal sealed class TextEditorOverlay
{
    private const double FrameTolerance = 6;
    private readonly CanvasView _owner;
    private readonly Canvas _host;
    private readonly TextBox _box;
    private TextEditSession? _session;
    private bool _syncing;
    private HandleKind _dragHandle;
    private Point _dragStartView;
    private Rect _dragStartBox;

    public TextEditorOverlay(CanvasView owner, Canvas host)
    {
        _owner = owner;
        _host = host;
        _box = CreateTextBox();
        _box.TextChanged += OnTextChanged;
        _box.PreviewKeyDown += OnPreviewKeyDown;
        _box.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        _box.Visibility = Visibility.Collapsed;
        _host.Children.Add(_box);
    }

    /// <summary>The editor control (exposed for focus checks and UI automation).</summary>
    public TextBox TextBox => _box;

    /// <summary>True when the editor has keyboard focus.</summary>
    public bool IsKeyboardFocusWithin => _box.IsKeyboardFocusWithin;

    /// <summary>Caret position requested for the next show (canvas point), or null for end of text.</summary>
    public Point? PendingCaretPoint { get; set; }

    /// <summary>Synchronizes the TextBox with the session (visibility, formatting, transform).</summary>
    public void Sync()
    {
        var session = _owner.Controller?.TextSession;
        if (session is null)
        {
            if (_session is not null)
            {
                _session = null;
                _box.Visibility = Visibility.Collapsed;
                if (_box.IsKeyboardFocusWithin)
                {
                    _owner.Viewport.Focus();
                }
            }

            return;
        }

        var isNewSession = !ReferenceEquals(session, _session);
        _session = session;
        var t = session.Text;
        _syncing = true;
        try
        {
            if (_box.Text != t.Text)
            {
                _box.Text = t.Text;
            }

            _box.FontFamily = new FontFamily(t.FontFamily);
            _box.FontSize = t.FontSizePx;
            _box.FontWeight = t.Bold ? FontWeights.Bold : FontWeights.Normal;
            _box.FontStyle = t.Italic ? FontStyles.Italic : FontStyles.Normal;
            _box.Width = Math.Max(TextLayoutEngine.MinBoxWidth, t.Box.Width);
            _box.MinHeight = t.Box.Height;
            var caret = t.Foreground;
            _box.CaretBrush = new SolidColorBrush(caret.A < 64 ? Colors.Black : Color.FromRgb(caret.R, caret.G, caret.B));
            var vt = _owner.Transform;
            var m = new Matrix(1, 0, 0, 1, t.Box.X, t.Box.Y);
            m.Append(t.Transform);
            m.Append(new Matrix(vt.Scale, 0, 0, vt.Scale, vt.Origin.X - vt.ScrollX, vt.Origin.Y - vt.ScrollY));
            _box.RenderTransform = new MatrixTransform(m);
            _box.Visibility = Visibility.Visible;
        }
        finally
        {
            _syncing = false;
        }

        if (isNewSession)
        {
            var caretPoint = PendingCaretPoint;
            PendingCaretPoint = null;
            _box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                _box.Focus();
                Keyboard.Focus(_box);
                if (caretPoint is { } cp && _session is not null)
                {
                    var local = ToBoxSpace(cp);
                    var idx = _box.GetCharacterIndexFromPoint(local, snapToText: true);
                    if (idx >= 0)
                    {
                        var rect = _box.GetRectFromCharacterIndex(idx);
                        _box.CaretIndex = local.X > rect.X + (_box.FontSize * 0.25) && idx < _box.Text.Length ? idx + 1 : idx;
                    }
                    else
                    {
                        _box.CaretIndex = _box.Text.Length;
                    }
                }
                else
                {
                    _box.CaretIndex = _box.Text.Length;
                }
            });
        }
    }

    /// <summary>The editing frame (effective box corners) in view coordinates.</summary>
    public IReadOnlyList<Point> FrameViewPolygon()
    {
        var t = _session?.Text ?? _owner.Controller?.TextSession?.Text;
        if (t is null)
        {
            return [];
        }

        var box = TextLayoutEngine.EffectiveBox(t);
        var vt = _owner.Transform;
        return new[] { box.TopLeft, box.TopRight, box.BottomRight, box.BottomLeft }
            .Select(p => vt.CanvasToView(t.Transform.Transform(p))).ToList();
    }

    /// <summary>Which part of the frame is at a view point (None when outside or inside the text area).</summary>
    public HandleKind HitTestFrame(Point view)
    {
        var t = _owner.Controller?.TextSession?.Text;
        if (t is null)
        {
            return HandleKind.None;
        }

        var local = ToTextSpace(view, t);
        var box = TextLayoutEngine.EffectiveBox(t);
        var tol = FrameTolerance / Math.Max(1e-6, _owner.Transform.Scale * TransformScale(t));
        var h = HandleBox.HitTest(box, local, tol);
        if (h is not HandleKind.Move and not HandleKind.None)
        {
            return h;
        }

        var outer = box;
        outer.Inflate(tol, tol);
        return outer.Contains(local) ? HandleKind.Move : HandleKind.None;
    }

    /// <summary>Cursor for a frame part.</summary>
    public static Cursor CursorFor(HandleKind h) => h switch
    {
        HandleKind.Move => Cursors.SizeAll,
        HandleKind.Left or HandleKind.Right => Cursors.SizeWE,
        HandleKind.Top or HandleKind.Bottom => Cursors.SizeNS,
        HandleKind.TopLeft or HandleKind.BottomRight => Cursors.SizeNWSE,
        HandleKind.TopRight or HandleKind.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    /// <summary>Starts moving/resizing the box (the box itself becomes selected: Delete removes it).</summary>
    public void BeginFrameDrag(HandleKind handle, Point view)
    {
        var t = _owner.Controller?.TextSession?.Text;
        if (t is null)
        {
            return;
        }

        _dragHandle = handle;
        _dragStartView = view;
        _dragStartBox = new Rect(t.Box.X, t.Box.Y, t.Box.Width, TextLayoutEngine.EffectiveBox(t).Height);
        _owner.Viewport.Focus();
    }

    /// <summary>Continues a frame drag.</summary>
    public void UpdateFrameDrag(Point view, ModifierKeys modifiers)
    {
        var t = _owner.Controller?.TextSession?.Text;
        if (t is null || _dragHandle == HandleKind.None)
        {
            return;
        }

        var a = ToTextSpace(_dragStartView, t);
        var b = ToTextSpace(view, t);
        var box = HandleBox.Resize(_dragStartBox, _dragHandle, b - a, keepAspect: false, minSize: 8);
        if (_dragHandle == HandleKind.Move)
        {
            box = new Rect(Math.Round(box.X), Math.Round(box.Y), box.Width, box.Height);
        }

        _ = modifiers;
        _owner.Controller!.EditorBoxChanged(box);
    }

    /// <summary>Ends a frame drag.</summary>
    public void EndFrameDrag() => _dragHandle = HandleKind.None;

    private static double TransformScale(TextObject t) => Math.Sqrt(Math.Abs(t.Transform.Determinant));

    private Point ToTextSpace(Point view, TextObject t)
    {
        var canvas = _owner.Transform.ViewToCanvas(view);
        var inv = t.Transform;
        if (!inv.HasInverse)
        {
            return canvas;
        }

        inv.Invert();
        return inv.Transform(canvas);
    }

    private Point ToBoxSpace(Point canvas)
    {
        var t = _session!.Text;
        var inv = t.Transform;
        if (inv.HasInverse)
        {
            inv.Invert();
            canvas = inv.Transform(canvas);
        }

        return new Point(canvas.X - t.Box.X, canvas.Y - t.Box.Y);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing && _session is not null)
        {
            _owner.Controller?.EditorTextChanged(_box.Text);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _owner.Controller?.CommitTextEdit();
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && _owner.Controller is { } c)
        {
            var canvas = _owner.Transform.ViewToCanvas(e.GetPosition(_owner.Viewport));
            if (c.EditorDoubleClick(canvas))
            {
                e.Handled = true;
            }
        }
    }

    private static TextBox CreateTextBox()
    {
        var template = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(Control.PaddingProperty, new Thickness(0));
        host.SetValue(FrameworkElement.FocusVisualStyleProperty, null);
        var svTemplate = new ControlTemplate(typeof(ScrollViewer));
        svTemplate.VisualTree = new FrameworkElementFactory(typeof(ScrollContentPresenter), "PART_ScrollContentPresenter");
        host.SetValue(Control.TemplateProperty, svTemplate);
        template.VisualTree = host;

        var box = new TextBox
        {
            Style = new Style(typeof(TextBox)),
            Template = template,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            SelectionBrush = SystemColors.HighlightBrush,
            SelectionOpacity = 0.4,
            MinWidth = 0,
            UseLayoutRounding = false,
            SnapsToDevicePixels = false,
            FocusVisualStyle = null,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            IsUndoEnabled = true,
        };
        TextOptions.SetTextFormattingMode(box, TextFormattingMode.Ideal);
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, "TextEditor");
        System.Windows.Automation.AutomationProperties.SetName(box, "Text editor");
        Canvas.SetLeft(box, 0);
        Canvas.SetTop(box, 0);
        return box;
    }
}
