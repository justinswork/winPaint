using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.App.ViewModels;

namespace WinPaint.App.Views;

/// <summary>Edit colors dialog. Code-behind only maps mouse positions on the spectrum to view-model calls.</summary>
public partial class EditColorsDialog : Window
{
    private readonly EditColorsViewModel _vm;
    private readonly Action<Color> _addCustom;

    /// <summary>Creates the dialog.</summary>
    public EditColorsDialog(EditColorsViewModel vm, Action<Color> addCustom)
    {
        InitializeComponent();
        _vm = vm;
        _addCustom = addCustom;
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
        Loaded += (_, _) => UpdateMarkers();
        Closed += (_, _) => vm.PropertyChanged -= OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => UpdateMarkers();

    private void UpdateMarkers()
    {
        Canvas.SetLeft(SquareMarker, (_vm.Saturation / 100 * Square.ActualWidth) - 6);
        Canvas.SetTop(SquareMarker, ((1 - (_vm.Value / 100)) * Square.ActualHeight) - 6);
        SquareMarker.Stroke = _vm.Value > 60 && _vm.Saturation < 40 ? Brushes.Black : Brushes.White;
        Canvas.SetTop(HueMarker, (_vm.Hue / 359 * HueBar.ActualHeight) - 2);
    }

    private void OnSquareDown(object sender, MouseButtonEventArgs e)
    {
        Square.CaptureMouse();
        PickSquare(e);
    }

    private void OnSquareMove(object sender, MouseEventArgs e)
    {
        if (Square.IsMouseCaptured)
        {
            PickSquare(e);
        }
    }

    private void OnSquareUp(object sender, MouseButtonEventArgs e) => Square.ReleaseMouseCapture();

    private void PickSquare(MouseEventArgs e)
    {
        var p = e.GetPosition(Square);
        _vm.PickSquare(p.X / Square.ActualWidth, p.Y / Square.ActualHeight);
    }

    private void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        _vm.PickHue(e.GetPosition(HueBar).Y / HueBar.ActualHeight);
    }

    private void OnHueMove(object sender, MouseEventArgs e)
    {
        if (HueBar.IsMouseCaptured)
        {
            _vm.PickHue(e.GetPosition(HueBar).Y / HueBar.ActualHeight);
        }
    }

    private void OnHueUp(object sender, MouseButtonEventArgs e) => HueBar.ReleaseMouseCapture();

    private void OnAddCustom(object sender, RoutedEventArgs e) => _addCustom(_vm.NewColor);

    private void OnOk(object sender, RoutedEventArgs e)
    {
        // Commit any focused text field before closing.
        if (Keyboard.FocusedElement is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        DialogResult = true;
    }
}
