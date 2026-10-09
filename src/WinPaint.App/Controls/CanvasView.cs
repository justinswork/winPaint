using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.View;

namespace WinPaint.App.Controls;

/// <summary>
/// The drawing surface: shows the document bitmap with zoom/scroll, rulers, gridlines, tool overlays and the
/// live text editor, and routes pointer input to the <see cref="ICanvasController"/>.
/// </summary>
public sealed partial class CanvasView : Grid
{
    /// <summary>Controller (the main view model).</summary>
    public static readonly DependencyProperty ControllerProperty = DependencyProperty.Register(
        nameof(Controller), typeof(ICanvasController), typeof(CanvasView), new PropertyMetadata(null, (d, e) => ((CanvasView)d).OnControllerChanged((ICanvasController?)e.OldValue, (ICanvasController?)e.NewValue)));

    /// <summary>Zoom factor (1 = 100 %), two-way.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CanvasView), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((CanvasView)d).OnZoomPropertyChanged((double)e.NewValue)));

    /// <summary>Show rulers.</summary>
    public static readonly DependencyProperty ShowRulersProperty = DependencyProperty.Register(
        nameof(ShowRulers), typeof(bool), typeof(CanvasView), new PropertyMetadata(false, (d, _) => ((CanvasView)d).UpdateRulerVisibility()));

    /// <summary>Show pixel gridlines (only drawn at high zoom).</summary>
    public static readonly DependencyProperty ShowGridlinesProperty = DependencyProperty.Register(
        nameof(ShowGridlines), typeof(bool), typeof(CanvasView), new PropertyMetadata(false, (d, _) => ((CanvasView)d)._overlay.InvalidateVisual()));

    /// <summary>Minimum zoom at which gridlines are drawn (400 %).</summary>
    public const double GridZoomThreshold = 4.0;

    private const double CanvasMargin = 24;
    private readonly ViewTransform _vt = new();
    private readonly ViewportPanel _viewport;
    private readonly Image _image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly SurroundLayer _surround;
    private readonly OverlayLayer _overlay;
    private readonly Canvas _editorHost = new() { ClipToBounds = false };
    private readonly ScrollBar _hScroll = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollBar _vScroll = new() { Orientation = Orientation.Vertical };
    private readonly Ruler _hRuler;
    private readonly Ruler _vRuler;
    private readonly Border _corner = new();
    private readonly TextEditorOverlay _editor;
    private readonly DispatcherTimer _antsTimer;
    private readonly DispatcherTimer _toolTimer;
    private PaintDocument? _doc;
    private bool _updatingZoom;

    /// <summary>Creates the control.</summary>
    public CanvasView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        _surround = new SurroundLayer(this);
        _overlay = new OverlayLayer(this);
        _hRuler = new Ruler(this, Orientation.Horizontal) { Height = 22 };
        _vRuler = new Ruler(this, Orientation.Vertical) { Width = 22 };
        _viewport = new ViewportPanel(this) { ClipToBounds = true, Background = Brushes.Transparent, Focusable = true, FocusVisualStyle = null };
        _viewport.Children.Add(_surround);
        _viewport.Children.Add(_image);
        _viewport.Children.Add(_overlay);
        _viewport.Children.Add(_editorHost);
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _image.IsHitTestVisible = false;
        _surround.IsHitTestVisible = false;
        _overlay.IsHitTestVisible = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_viewport, "Canvas");
        System.Windows.Automation.AutomationProperties.SetName(_viewport, "Canvas");

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(_corner, 0, 0);
        Add(_hRuler, 0, 1);
        Add(_vRuler, 1, 0);
        Add(_viewport, 1, 1);
        Add(_vScroll, 1, 2);
        Add(_hScroll, 2, 1);
        _corner.SetResourceReference(Border.BackgroundProperty, "RulerBackgroundBrush");
        UpdateRulerVisibility();

        _hScroll.Scroll += (_, _) => ScrollTo(_hScroll.Value, _vt.ScrollY);
        _vScroll.Scroll += (_, _) => ScrollTo(_vt.ScrollX, _vScroll.Value);
        _viewport.SizeChanged += (_, _) => UpdateView();
        _editor = new TextEditorOverlay(this, _editorHost);

        _antsTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(150) };
        _antsTimer.Tick += (_, _) =>
        {
            _overlay.AntsPhase = (_overlay.AntsPhase + 1) % 8;
            _overlay.InvalidateVisual();
        };
        _toolTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _toolTimer.Tick += (_, _) => Controller?.Tick();
        HookInput();
        Loaded += (_, _) =>
        {
            _vt.DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            UpdateView();
        };
        Unloaded += (_, _) =>
        {
            _antsTimer.Stop();
            _toolTimer.Stop();
            StopRendering();
        };
    }

    /// <summary>Raised when the view (zoom/scroll/size) changed.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Controller (view model).</summary>
    public ICanvasController? Controller
    {
        get => (ICanvasController?)GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    /// <summary>Zoom factor.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Show rulers.</summary>
    public bool ShowRulers
    {
        get => (bool)GetValue(ShowRulersProperty);
        set => SetValue(ShowRulersProperty, value);
    }

    /// <summary>Show gridlines.</summary>
    public bool ShowGridlines
    {
        get => (bool)GetValue(ShowGridlinesProperty);
        set => SetValue(ShowGridlinesProperty, value);
    }

    /// <summary>The current view transform (read-only use).</summary>
    public ViewTransform Transform => _vt;

    /// <summary>The viewport element (input surface).</summary>
    public FrameworkElement Viewport => _viewport;

    /// <summary>The document shown.</summary>
    internal PaintDocument? Document => _doc;

    /// <summary>Overlay layer.</summary>
    internal OverlayLayer OverlayLayer => _overlay;

    /// <summary>Text editor overlay.</summary>
    internal TextEditorOverlay Editor => _editor;

    /// <summary>Last canvas position of the pointer (for rulers), or null.</summary>
    internal Point? PointerCanvas { get; private set; }

    /// <summary>Visible part of the canvas in canvas pixels.</summary>
    public PixelRect VisibleCanvasRect
    {
        get
        {
            if (_doc is null)
            {
                return PixelRect.Empty;
            }

            var a = _vt.ViewToCanvas(new Point(0, 0));
            var b = _vt.ViewToCanvas(new Point(_viewport.ActualWidth, _viewport.ActualHeight));
            return PixelRect.FromRect(new Rect(a, b)).Intersect(_doc.Bounds);
        }
    }

    /// <summary>Zooms one preset step around the viewport center.</summary>
    public void ZoomStep(bool zoomIn) => SetZoom(zoomIn ? ViewTransform.NextPreset(_vt.Zoom) : ViewTransform.PreviousPreset(_vt.Zoom), null);

    /// <summary>Fits the whole canvas in the viewport.</summary>
    public void ZoomToFit()
    {
        if (_doc is null)
        {
            return;
        }

        SetZoom(ViewTransform.FitZoom(_doc.Width, _doc.Height, _viewport.ActualWidth, _viewport.ActualHeight, _vt.DpiScale, CanvasMargin), null);
    }

    /// <summary>Sets the zoom, keeping <paramref name="anchorView"/> (or the viewport center) fixed.</summary>
    public void SetZoom(double zoom, Point? anchorView)
    {
        zoom = Math.Clamp(zoom, ViewTransform.MinZoom, ViewTransform.MaxZoom);
        var anchor = anchorView ?? new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);
        var canvasPt = _vt.ViewToCanvas(anchor);
        _vt.Zoom = zoom;
        var (ox, oy) = ComputeOrigin();
        _vt.ScrollX = (canvasPt.X * _vt.Scale) + ox - anchor.X;
        _vt.ScrollY = (canvasPt.Y * _vt.Scale) + oy - anchor.Y;
        _updatingZoom = true;
        Zoom = zoom;
        _updatingZoom = false;
        UpdateView();
    }

    /// <summary>Scrolls by a delta in DIPs.</summary>
    public void ScrollBy(double dx, double dy) => ScrollTo(_vt.ScrollX + dx, _vt.ScrollY + dy);

    /// <summary>Creates a dense snapshot of what the viewport currently shows (for tests/diagnostics).</summary>
    internal void InvalidateOverlay() => _overlay.InvalidateVisual();

    private void Add(UIElement e, int row, int col)
    {
        SetRow(e, row);
        SetColumn(e, col);
        Children.Add(e);
    }

    private void UpdateRulerVisibility()
    {
        var v = ShowRulers ? Visibility.Visible : Visibility.Collapsed;
        _hRuler.Visibility = v;
        _vRuler.Visibility = v;
        _corner.Visibility = v;
    }

    private void OnZoomPropertyChanged(double z)
    {
        if (!_updatingZoom && Math.Abs(z - _vt.Zoom) > 1e-9)
        {
            SetZoom(z, null);
        }
    }

    private void OnControllerChanged(ICanvasController? old, ICanvasController? c)
    {
        if (old is not null)
        {
            old.DocumentReplaced -= OnDocumentReplaced;
            old.OverlayChanged -= OnOverlayChanged;
            old.ZoomRequested -= OnZoomRequested;
        }

        if (c is not null)
        {
            c.DocumentReplaced += OnDocumentReplaced;
            c.OverlayChanged += OnOverlayChanged;
            c.ZoomRequested += OnZoomRequested;
        }

        AttachDocument(c?.Document);
    }

    private void OnDocumentReplaced(object? sender, EventArgs e)
    {
        AttachDocument(Controller?.Document);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ZoomToFit);
    }

    private void OnZoomRequested(object? sender, ZoomRequest e)
    {
        var anchor = _vt.CanvasToView(e.CanvasPoint);
        SetZoom(e.ZoomIn ? ViewTransform.NextPreset(_vt.Zoom) : ViewTransform.PreviousPreset(_vt.Zoom), anchor);
    }

    private void OnOverlayChanged(object? sender, EventArgs e)
    {
        var overlay = Controller?.Overlay;
        var needAnts = overlay is not null && (overlay.Ants is not null || overlay.AntsOpen is not null || overlay.HandleBox is not null);
        if (needAnts && !_antsTimer.IsEnabled)
        {
            _antsTimer.Start();
        }
        else if (!needAnts && _antsTimer.IsEnabled)
        {
            _antsTimer.Stop();
        }

        _editor.Sync();
        _overlay.InvalidateVisual();
        UpdateCursor();
    }

    private (double X, double Y) ComputeOrigin()
    {
        if (_doc is null)
        {
            return (CanvasMargin, CanvasMargin);
        }

        var s = _vt.Scale;
        var cw = _doc.Width * s;
        var ch = _doc.Height * s;
        var vw = _viewport.ActualWidth;
        var vh = _viewport.ActualHeight;
        var ox = cw + (2 * CanvasMargin) <= vw ? Snap((vw - cw) / 2) : CanvasMargin;
        var oy = ch + (2 * CanvasMargin) <= vh ? Snap((vh - ch) / 2) : CanvasMargin;
        return (ox, oy);
    }

    private double Snap(double dip) => Math.Round(dip * _vt.DpiScale) / _vt.DpiScale;

    private void ScrollTo(double x, double y)
    {
        _vt.ScrollX = x;
        _vt.ScrollY = y;
        UpdateView();
    }

    /// <summary>Recomputes origin, clamps scroll, positions the image, scrollbars, rulers and overlays.</summary>
    internal void UpdateView()
    {
        if (_doc is null || _viewport.ActualWidth <= 0)
        {
            return;
        }

        var s = _vt.Scale;
        var (ox, oy) = ComputeOrigin();
        _vt.Origin = new Vector(ox, oy);
        var contentW = (_doc.Width * s) + (2 * CanvasMargin);
        var contentH = (_doc.Height * s) + (2 * CanvasMargin);
        var maxX = Math.Max(0, contentW - _viewport.ActualWidth);
        var maxY = Math.Max(0, contentH - _viewport.ActualHeight);
        _vt.ScrollX = Snap(Math.Clamp(_vt.ScrollX, 0, maxX));
        _vt.ScrollY = Snap(Math.Clamp(_vt.ScrollY, 0, maxY));
        if (maxX <= 0)
        {
            _vt.ScrollX = 0;
        }

        if (maxY <= 0)
        {
            _vt.ScrollY = 0;
        }

        ConfigureScrollBar(_hScroll, maxX, _viewport.ActualWidth, _vt.ScrollX);
        ConfigureScrollBar(_vScroll, maxY, _viewport.ActualHeight, _vt.ScrollY);

        _image.Width = _doc.Width;
        _image.Height = _doc.Height;
        _image.RenderTransform = new MatrixTransform(s, 0, 0, s, ox - _vt.ScrollX, oy - _vt.ScrollY);
        RenderOptions.SetBitmapScalingMode(_image, _vt.Zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        _surround.InvalidateVisual();
        _overlay.InvalidateVisual();
        _hRuler.InvalidateVisual();
        _vRuler.InvalidateVisual();
        _editor.Sync();
        UpdateCursor();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ConfigureScrollBar(ScrollBar bar, double max, double viewport, double value)
    {
        bar.Visibility = max > 0 ? Visibility.Visible : Visibility.Collapsed;
        bar.Minimum = 0;
        bar.Maximum = max;
        bar.ViewportSize = viewport;
        bar.LargeChange = viewport * 0.9;
        bar.SmallChange = 40;
        bar.Value = value;
    }

    /// <summary>Panel that stretches every child over the full viewport.</summary>
    private sealed class ViewportPanel(CanvasView view) : Panel
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new CanvasAutomationPeer(this, view);

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (UIElement c in InternalChildren)
            {
                c.Measure(availableSize);
            }

            return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (UIElement c in InternalChildren)
            {
                if (c is Image img)
                {
                    img.Arrange(double.IsNaN(img.Width) ? default : new Rect(0, 0, img.Width, img.Height));
                }
                else
                {
                    c.Arrange(new Rect(finalSize));
                }
            }

            return finalSize;
        }
    }
}
