using System.Windows;
using WinPaint.App.ViewModels;

namespace WinPaint.App.Views;

/// <summary>Resize and skew dialog (Ctrl+W).</summary>
public partial class ResizeSkewDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public ResizeSkewDialog(ResizeSkewViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) =>
        {
            H.Focus();
            H.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResizeSkewViewModel { IsValid: true })
        {
            DialogResult = true;
        }
    }
}
