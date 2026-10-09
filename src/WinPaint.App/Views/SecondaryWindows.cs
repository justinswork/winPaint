using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinPaint.App.Controls;
using WinPaint.App.Resources;
using WinPaint.App.ViewModels;
using WinPaint.Core.Imaging;

namespace WinPaint.App.Views;

/// <summary>Full-screen view: only the image, centered on black. Esc or a click exits.</summary>
public sealed class FullScreenWindow : Window
{
    /// <summary>Creates the window for a flattened image.</summary>
    public FullScreenWindow(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Background = Brushes.Black;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "FullScreenWindow");
        var img = new Image { Source = image.ToBitmapSource(), Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Content = new Viewbox { Child = img, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.F11)
            {
                Close();
            }
        };
        MouseDown += (_, _) => Close();
        Loaded += (_, _) =>
        {
            // Show 1 image pixel = 1 device pixel when it fits.
            var dpi = VisualTreeHelper.GetDpi(this);
            img.LayoutTransform = new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY);
        };
    }
}

/// <summary>Floating thumbnail of the whole image with the visible area; click or drag to navigate.</summary>
public sealed class ThumbnailWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly CanvasView _canvas;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Rectangle _viewRect = new() { Stroke = Brushes.DodgerBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 30, 144, 255)), IsHitTestVisible = false };
    private readonly Canvas _overlay = new();
    private readonly DispatcherTimer _refresh;
    private Core.Document.PaintDocument _doc;
    private bool _dirty = true;

    /// <summary>Creates the window.</summary>
    public ThumbnailWindow(MainViewModel vm, CanvasView canvas)
    {
        _vm = vm;
        _canvas = canvas;
        _doc = vm.Document;
        Title = Strings.Dlg_Thumbnail;
        Width = 260;
        Height = 220;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "ThumbnailWindow");
        var grid = new Grid { Margin = new Thickness(6) };
        grid.Children.Add(_image);
        _overlay.Children.Add(_viewRect);
        grid.Children.Add(_overlay);
        Content = grid;
        grid.MouseLeftButtonDown += (_, e) =>
        {
            grid.CaptureMouse();
            Navigate(e.GetPosition(_image));
        };
        grid.MouseMove += (_, e) =>
        {
            if (grid.IsMouseCaptured)
            {
                Navigate(e.GetPosition(_image));
            }
        };
        grid.MouseLeftButtonUp += (_, _) => grid.ReleaseMouseCapture();
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => RefreshImage(), Dispatcher);
        _canvas.ViewChanged += OnViewChanged;
        SizeChanged += (_, _) => UpdateRect();
        Closed += (_, _) =>
        {
            _refresh.Stop();
            _canvas.ViewChanged -= OnViewChanged;
            _doc.Invalidated -= OnInvalidated;
            _vm.DocumentReplaced -= OnReplaced;
        };
        _doc.Invalidated += OnInvalidated;
        _vm.DocumentReplaced += OnReplaced;
        RefreshImage();
    }

    private void OnReplaced(object? sender, EventArgs e)
    {
        _doc.Invalidated -= OnInvalidated;
        _doc = _vm.Document;
        _doc.Invalidated += OnInvalidated;
        _dirty = true;
        RefreshImage();
    }

    private void OnInvalidated(object? sender, PixelRect e) => _dirty = true;

    private void OnViewChanged(object? sender, EventArgs e) => UpdateRect();

    private void RefreshImage()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        var doc = _vm.Document;
        var scale = Math.Min(1.0, Math.Min(400.0 / doc.Width, 400.0 / doc.Height));
        var small = Resampler.Resize(doc.Flatten(includeFloating: true), Math.Max(1, (int)(doc.Width * scale)), Math.Max(1, (int)(doc.Height * scale)), ResampleMode.HighQuality);
        _image.Source = small.ToBitmapSource();
        UpdateRect();
    }

    private Rect ImageRect()
    {
        var doc = _vm.Document;
        var aw = _image.ActualWidth;
        var ah = _image.ActualHeight;
        var s = Math.Min(aw / doc.Width, ah / doc.Height);
        var w = doc.Width * s;
        var h = doc.Height * s;
        return new Rect((aw - w) / 2, (ah - h) / 2, w, h);
    }

    private void UpdateRect()
    {
        var doc = _vm.Document;
        var r = ImageRect();
        if (r.Width <= 0)
        {
            return;
        }

        var vis = _canvas.VisibleCanvasRect;
        var s = r.Width / doc.Width;
        Canvas.SetLeft(_viewRect, r.X + (vis.X * s));
        Canvas.SetTop(_viewRect, r.Y + (vis.Y * s));
        _viewRect.Width = Math.Max(2, vis.Width * s);
        _viewRect.Height = Math.Max(2, vis.Height * s);
    }

    private void Navigate(Point p)
    {
        var doc = _vm.Document;
        var r = ImageRect();
        if (r.Width <= 0)
        {
            return;
        }

        var cx = (p.X - r.X) / r.Width * doc.Width;
        var cy = (p.Y - r.Y) / r.Height * doc.Height;
        var vis = _canvas.VisibleCanvasRect;
        var vt = _canvas.Transform;
        _canvas.ScrollBy((cx - (vis.X + (vis.Width / 2.0))) * vt.Scale, (cy - (vis.Y + (vis.Height / 2.0))) * vt.Scale);
    }
}
