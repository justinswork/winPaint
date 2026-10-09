using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using WinPaint.App.Services;
using WinPaint.App.ViewModels;
using WinPaint.Core.Imaging;

namespace WinPaint.App.Views;

/// <summary>
/// The main window. Code-behind is limited to view concerns: keyboard routing to view-model commands, drag-and-drop,
/// window placement, the close prompt and hosting secondary windows.
/// </summary>
public partial class MainWindow : Window, IViewService
{
    private const int WmMouseHWheel = 0x020E;
    private MainViewModel? _vm;
    private SettingsService? _settings;
    private ThumbnailWindow? _thumbnail;
    private bool _closingAllowed;

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource src)
            {
                src.AddHook(WndProc);
            }
        };
        ContentRendered += (_, _) =>
            StartupTime = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
    }

    /// <inheritdoc/>
    public PixelRect VisibleCanvasRect => Canvas.VisibleCanvasRect;

    /// <summary>Time from process start to the first rendered frame (cold-start measurement).</summary>
    public TimeSpan? StartupTime { get; private set; }

    /// <summary>Connects the view model and restores the window placement.</summary>
    public void Attach(MainViewModel vm, SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(settings);
        _vm = vm;
        _settings = settings;
        DataContext = vm;
        vm.View = this;
        vm.ContextMenuRequested += (_, _) =>
        {
            if (Resources["SelectionMenu"] is ContextMenu menu)
            {
                menu.DataContext = vm;
                menu.PlacementTarget = Canvas.Viewport;
                menu.Placement = PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy))
            {
                Mouse.OverrideCursor = vm.IsBusy ? Cursors.Wait : null;

                // Keep the window responsive (it repaints and can be moved) but block edits while the document is busy.
                RootGrid.IsHitTestVisible = !vm.IsBusy;
            }
            else if (e.PropertyName == nameof(MainViewModel.RecentFiles))
            {
                vm.RecentFiles.CollectionChanged += (_, _) => BuildRecentMenu();
                BuildRecentMenu();
            }
        };
        vm.RecentFiles.CollectionChanged += (_, _) => BuildRecentMenu();
        BuildRecentMenu();

        var s = settings.Current;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (s.WindowLeft is { } l && s.WindowTop is { } t && IsOnScreen(l, t))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        Loaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Canvas.ZoomToFit);
        Canvas.AutomationState = () => AutomationBridge.State(vm, this);
        Canvas.ExternalAutomationCommand = cmd => AutomationBridge.Execute(cmd, this, vm);
    }

    /// <summary>The window's root element (for snapshots).</summary>
    internal FrameworkElement RootElement => RootGrid;

    /// <inheritdoc/>
    public void ZoomToFit() => Canvas.ZoomToFit();

    /// <inheritdoc/>
    public void CloseWindow()
    {
        _closingAllowed = true;
        Close();
    }

    /// <inheritdoc/>
    public void ShowFullScreen()
    {
        if (_vm is null)
        {
            return;
        }

        var w = new FullScreenWindow(_vm.Document.Flatten()) { Owner = this };
        w.ShowDialog();
    }

    /// <inheritdoc/>
    public void ShowThumbnail(bool show)
    {
        if (_vm is null)
        {
            return;
        }

        if (show && _thumbnail is null)
        {
            _thumbnail = new ThumbnailWindow(_vm, Canvas) { Owner = this };
            _thumbnail.Closed += (_, _) =>
            {
                _thumbnail = null;
                _vm.ShowThumbnail = false;
            };
            _thumbnail.Show();
        }
        else if (!show && _thumbnail is not null)
        {
            _thumbnail.Close();
        }
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnClosing(e);
        if (_closingAllowed || _vm is null)
        {
            SavePlacement();
            return;
        }

        e.Cancel = true;
        Dispatcher.BeginInvoke(async () =>
        {
            if (await _vm.CanCloseAsync())
            {
                _closingAllowed = true;
                Close();
            }
        });
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || _vm is null)
        {
            return;
        }

        e.Handled = HandleShortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
    }

    /// <inheritdoc/>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e?.Key == Key.Space)
        {
            Canvas.UpdateCursor();
        }
    }

    /// <summary>Routes a keyboard shortcut (§5.12) to its command. Returns true when handled.</summary>
    internal bool HandleShortcut(Key key, ModifierKeys mods)
    {
        var vm = _vm!;
        if (vm.IsBusy)
        {
            // A long operation is running in the background; ignore shortcuts until it completes.
            return true;
        }

        var ctrl = (mods & ModifierKeys.Control) != 0;
        var shift = (mods & ModifierKeys.Shift) != 0;
        var typing = Keyboard.FocusedElement is TextBoxBase;
        if (ctrl)
        {
            switch (key)
            {
                case Key.N: Run(vm.NewCommand); return true;
                case Key.O: Run(vm.OpenCommand); return true;
                case Key.S when shift: Run(vm.SaveAsCommand); return true;
                case Key.S: Run(vm.SaveCommand); return true;
                case Key.P: Run(vm.PrintCommand); return true;
                case Key.E: Run(vm.ImagePropertiesCommand); return true;
                case Key.Z when shift: Run(vm.RedoCommand); return true;
                case Key.Z: Run(vm.UndoCommand); return true;
                case Key.Y: Run(vm.RedoCommand); return true;
                case Key.X when shift: Run(vm.CropCommand); return true;
                case Key.X when !typing: Run(vm.CutCommand); return true;
                case Key.C when !typing: Run(vm.CopyCommand); return true;
                case Key.V when !typing: Run(vm.PasteCommand); return true;
                case Key.A when !typing: Run(vm.SelectAllCommand); return true;
                case Key.I when shift: Run(vm.InvertColorsCommand); return true;
                case Key.W: Run(vm.ResizeSkewCommand); return true;
                case Key.R: Run(vm.ToggleRulersCommand); return true;
                case Key.G: Run(vm.ToggleGridlinesCommand); return true;
                case Key.B: Run(vm.ToggleBoldCommand); return true;
                case Key.I: Run(vm.ToggleItalicCommand); return true;
                case Key.U: Run(vm.ToggleUnderlineCommand); return true;
                case Key.PageUp: Run(vm.ZoomInCommand); return true;
                case Key.PageDown: Run(vm.ZoomOutCommand); return true;
                case Key.OemPlus or Key.Add: vm.SizeOrZoom(increase: true); return true;
                case Key.OemMinus or Key.Subtract: vm.SizeOrZoom(increase: false); return true;
                case Key.D0 or Key.NumPad0: Run(vm.ZoomFitCommand); return true;
                case Key.D1 or Key.NumPad1: Run(vm.ZoomActualCommand); return true;
            }

            return false;
        }

        switch (key)
        {
            case Key.F12: Run(vm.SaveAsCommand); return true;
            case Key.F11: Run(vm.FullScreenCommand); return true;
            case Key.F1: Run(vm.ShowShortcutsCommand); return true;
            case Key.Escape: return vm.HandleEscape();
        }

        if (typing)
        {
            return false;
        }

        switch (key)
        {
            case Key.Delete: Run(vm.DeleteCommand); return true;
            case Key.X when mods == ModifierKeys.None: Run(vm.SwapColorsCommand); return true;
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter when Keyboard.FocusedElement is not ButtonBase and not Slider and not ListBoxItem:
                return vm.ForwardKey(key, mods);
            case Key.Space:
                Canvas.UpdateCursor();
                return false;
        }

        return false;
    }

    private void OnZoomBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Canvas.Viewport.Focus();
            e.Handled = true;
        }
    }

    private static void Run(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm is not null && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            e.Handled = true;
            await _vm.OpenDroppedAsync(files[0]);
        }
    }

    private void BuildRecentMenu()
    {
        if (_vm is null)
        {
            return;
        }

        RecentMenu.Items.Clear();
        if (_vm.RecentFiles.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = WinPaint.App.Resources.Strings.File_NoRecent, IsEnabled = false });
            return;
        }

        var i = 1;
        foreach (var path in _vm.RecentFiles)
        {
            RecentMenu.Items.Add(new MenuItem
            {
                Header = $"_{i++ % 10} {System.IO.Path.GetFileName(path).Replace("_", "__", StringComparison.Ordinal)}",
                ToolTip = path,
                Command = _vm.OpenRecentCommand,
                CommandParameter = path,
            });
        }

        RecentMenu.Items.Add(new Separator());
        RecentMenu.Items.Add(new MenuItem { Header = WinPaint.App.Resources.Strings.File_ClearRecent, Command = _vm.ClearRecentCommand });
    }

    private void SavePlacement()
    {
        if (_settings is null)
        {
            return;
        }

        var s = _settings.Current;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowLeft = r.Left;
        s.WindowTop = r.Top;
        s.WindowWidth = r.Width;
        s.WindowHeight = r.Height;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        _vm?.SaveSettings();
        _settings.Save();
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50 && top >= SystemParameters.VirtualScreenTop - 50
        && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
        && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseHWheel && Canvas.IsMouseOver)
        {
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            Canvas.OnHorizontalWheel(delta);
            handled = true;
        }

        return IntPtr.Zero;
    }
}
