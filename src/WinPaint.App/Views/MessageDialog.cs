using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinPaint.App.Views;

/// <summary>A themed message box with arbitrary buttons (e.g. Save / Don't save / Cancel).</summary>
public sealed class MessageDialog : Window
{
    private int _result = -1;

    private MessageDialog(string title, string message, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MinWidth = 360;
        MaxWidth = 560;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "MessageDialog");
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 500, FontSize = 14 });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var b = new Button { Content = buttons[i], MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = i == defaultIndex, IsCancel = i == cancelIndex };
            System.Windows.Automation.AutomationProperties.SetAutomationId(b, "DialogButton" + i);
            b.Click += (_, _) =>
            {
                _result = index;
                Close();
            };
            bar.Children.Add(b);
        }

        root.Children.Add(bar);
        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && cancelIndex >= 0)
            {
                _result = cancelIndex;
                Close();
            }
        };
    }

    /// <summary>Shows the dialog and returns the clicked button index (the cancel index when closed).</summary>
    public static int Show(Window? owner, string title, string message, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        var d = new MessageDialog(title, message, buttons, defaultIndex, cancelIndex < 0 ? buttons.Count - 1 : cancelIndex);
        if (owner is { IsLoaded: true })
        {
            d.Owner = owner;
        }
        else
        {
            d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        d.ShowDialog();
        return d._result < 0 ? (cancelIndex < 0 ? buttons.Count - 1 : cancelIndex) : d._result;
    }
}
