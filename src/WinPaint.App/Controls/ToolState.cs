using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace WinPaint.App.Controls;

/// <summary>Attached "active tool" highlight state for toolbar buttons (avoids toggle buttons fighting bindings).</summary>
public static class ToolState
{
    /// <summary>Highlighted as the active choice.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(ToolState), new FrameworkPropertyMetadata(false));

    /// <summary>Gets the value.</summary>
    public static bool GetIsActive(DependencyObject d) => (bool)(d ?? throw new ArgumentNullException(nameof(d))).GetValue(IsActiveProperty);

    /// <summary>Sets the value.</summary>
    public static void SetIsActive(DependencyObject d, bool value) => (d ?? throw new ArgumentNullException(nameof(d))).SetValue(IsActiveProperty, value);
}

/// <summary>Opens a button's <see cref="FrameworkElement.ContextMenu"/> as a dropdown when the button is clicked.</summary>
public static class DropDown
{
    /// <summary>Enables the behavior.</summary>
    public static readonly DependencyProperty OpensMenuProperty = DependencyProperty.RegisterAttached(
        "OpensMenu", typeof(bool), typeof(DropDown), new PropertyMetadata(false, OnOpensMenuChanged));

    /// <summary>Gets the value.</summary>
    public static bool GetOpensMenu(DependencyObject d) => (bool)(d ?? throw new ArgumentNullException(nameof(d))).GetValue(OpensMenuProperty);

    /// <summary>Sets the value.</summary>
    public static void SetOpensMenu(DependencyObject d, bool value) => (d ?? throw new ArgumentNullException(nameof(d))).SetValue(OpensMenuProperty, value);

    private static void OnOpensMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ButtonBase b && e.NewValue is true)
        {
            b.Click += (_, _) =>
            {
                if (b.ContextMenu is { } menu)
                {
                    menu.DataContext = b.DataContext;
                    menu.PlacementTarget = b;
                    menu.Placement = PlacementMode.Bottom;
                    menu.IsOpen = true;
                }
            };
        }
    }
}

/// <summary>True when all bound values are equal (for gallery highlight).</summary>
public sealed class AllEqualConverter : IMultiValueConverter
{
    /// <inheritdoc/>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is { Length: >= 2 } && values.Skip(1).All(v => Equals(v, values[0]));

    /// <inheritdoc/>
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}

/// <summary>Popup helper that closes when an item inside is clicked.</summary>
public static class PopupClose
{
    /// <summary>Marks a button that closes its containing popup when clicked.</summary>
    public static readonly DependencyProperty ClosesPopupProperty = DependencyProperty.RegisterAttached(
        "ClosesPopup", typeof(bool), typeof(PopupClose), new PropertyMetadata(false, OnChanged));

    /// <summary>Gets the value.</summary>
    public static bool GetClosesPopup(DependencyObject d) => (bool)(d ?? throw new ArgumentNullException(nameof(d))).GetValue(ClosesPopupProperty);

    /// <summary>Sets the value.</summary>
    public static void SetClosesPopup(DependencyObject d, bool value) => (d ?? throw new ArgumentNullException(nameof(d))).SetValue(ClosesPopupProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ButtonBase b && e.NewValue is true)
        {
            b.Click += (_, _) =>
            {
                DependencyObject? cur = b;
                while (cur is not null)
                {
                    if (cur is Popup p)
                    {
                        p.IsOpen = false;
                        return;
                    }

                    cur = LogicalTreeHelper.GetParent(cur) ?? System.Windows.Media.VisualTreeHelper.GetParent(cur);
                }
            };
        }
    }
}
