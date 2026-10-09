using System.Windows;
using WinPaint.App.ViewModels;

namespace WinPaint.App.Views;

/// <summary>Image properties dialog (Ctrl+E).</summary>
public partial class ImagePropertiesDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public ImagePropertiesDialog(ImagePropertiesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDefault(object sender, RoutedEventArgs e) => ((ImagePropertiesViewModel)DataContext).ResetDefaults();
}
