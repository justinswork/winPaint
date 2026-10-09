using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Imaging;

namespace WinPaint.App.Views;

/// <summary>Page setup: orientation, margins, scaling (adjust to % or fit to pages) and centering.</summary>
public static class PageSetupDialog
{
    /// <summary>Shows the dialog; returns true when accepted (settings are saved).</summary>
    public static bool Show(Window owner, SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var s = settings.Current;
        var w = DialogKit.Create(Strings.Dlg_PageSetup, "PageSetupDialog", 440);
        w.Owner = owner;
        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(DialogKit.Heading(Strings.Dlg_Orientation));
        var portrait = new RadioButton { Content = Strings.Dlg_Portrait, GroupName = "o", IsChecked = !s.PrintLandscape, Margin = new Thickness(0, 0, 16, 0) };
        var landscape = new RadioButton { Content = Strings.Dlg_Landscape, GroupName = "o", IsChecked = s.PrintLandscape };
        System.Windows.Automation.AutomationProperties.SetAutomationId(landscape, "Landscape");
        root.Children.Add(Row(portrait, landscape));

        root.Children.Add(DialogKit.Heading(Strings.Dlg_Margins));
        var m = s.PrintMargins is { Length: 4 } a ? a : [0.75, 0.75, 0.75, 0.75];
        var left = DialogKit.NumberBox(m[0], "MarginLeft");
        var top = DialogKit.NumberBox(m[1], "MarginTop");
        var right = DialogKit.NumberBox(m[2], "MarginRight");
        var bottom = DialogKit.NumberBox(m[3], "MarginBottom");
        root.Children.Add(Row(new Label { Content = Strings.Dlg_Left, Target = left }, left, new Label { Content = Strings.Dlg_Right, Target = right }, right));
        root.Children.Add(Row(new Label { Content = Strings.Dlg_Top, Target = top }, top, new Label { Content = Strings.Dlg_Bottom, Target = bottom }, bottom));

        root.Children.Add(DialogKit.Heading(Strings.Dlg_Scaling));
        var fit = s.PrintFitWide > 0 && s.PrintFitTall > 0;
        var adjust = new RadioButton { Content = Strings.Dlg_AdjustTo, GroupName = "s", IsChecked = !fit };
        var scale = DialogKit.NumberBox(s.PrintScalePercent, "ScalePercent");
        root.Children.Add(Row(adjust, scale, new TextBlock { Text = Strings.Dlg_NormalSize, VerticalAlignment = VerticalAlignment.Center }));
        var fitRb = new RadioButton { Content = Strings.Dlg_FitTo, GroupName = "s", IsChecked = fit };
        System.Windows.Automation.AutomationProperties.SetAutomationId(fitRb, "FitTo");
        var wide = DialogKit.NumberBox(Math.Max(1, s.PrintFitWide), "FitWide");
        var tall = DialogKit.NumberBox(Math.Max(1, s.PrintFitTall), "FitTall");
        wide.Width = 44;
        tall.Width = 44;
        root.Children.Add(Row(fitRb, wide, new TextBlock { Text = Strings.Dlg_PagesWideBy, VerticalAlignment = VerticalAlignment.Center }, tall, new TextBlock { Text = Strings.Dlg_PagesTall, VerticalAlignment = VerticalAlignment.Center }));

        root.Children.Add(DialogKit.Heading(Strings.Dlg_Centering));
        var ch = new CheckBox { Content = Strings.Dlg_CenterH, IsChecked = s.PrintCenterH, Margin = new Thickness(0, 0, 16, 0) };
        var cv = new CheckBox { Content = Strings.Dlg_CenterV, IsChecked = s.PrintCenterV };
        root.Children.Add(Row(ch, cv));

        root.Children.Add(DialogKit.Buttons(w, cancel: true));
        w.Content = root;
        if (w.ShowDialog() != true)
        {
            return false;
        }

        s.PrintLandscape = landscape.IsChecked == true;
        s.PrintMargins =
        [
            Math.Clamp(DialogKit.Parse(left, m[0]), 0, 5), Math.Clamp(DialogKit.Parse(top, m[1]), 0, 5),
            Math.Clamp(DialogKit.Parse(right, m[2]), 0, 5), Math.Clamp(DialogKit.Parse(bottom, m[3]), 0, 5),
        ];
        s.PrintScalePercent = (int)Math.Clamp(DialogKit.Parse(scale, s.PrintScalePercent), 1, 1000);
        s.PrintFitWide = fitRb.IsChecked == true ? (int)Math.Clamp(DialogKit.Parse(wide, 1), 1, 50) : 0;
        s.PrintFitTall = fitRb.IsChecked == true ? (int)Math.Clamp(DialogKit.Parse(tall, 1), 1, 50) : 0;
        s.PrintCenterH = ch.IsChecked == true;
        s.PrintCenterV = cv.IsChecked == true;
        settings.Save();
        return true;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var c in children)
        {
            if (c is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
            }

            p.Children.Add(c);
        }

        return p;
    }
}

/// <summary>Printing and print preview.</summary>
public static class Printing
{
    /// <summary>Default page size (Letter) used when no printer information is available.</summary>
    public static Size DefaultPage { get; } = new(8.5 * 96, 11 * 96);

    /// <summary>Shows the system print dialog and prints.</summary>
    public static void Print(Window owner, SettingsService settings, PixelBuffer image, double dpiX, double dpiY, string jobName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var dlg = new PrintDialog();
        var ps = PageSettings.From(settings.Current);
        dlg.PrintTicket.PageOrientation = ps.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var page = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
            var layout = new PrintLayout(image, dpiX, dpiY, page, ps);
            dlg.PrintDocument(layout.CreatePaginator(), jobName);
        }
        catch (PrintingCanceledException)
        {
        }
        catch (PrintQueueException ex)
        {
            MessageDialog.Show(owner, Strings.AppName, string.Format(CultureInfo.CurrentCulture, Strings.Msg_PrintFailed, ex.Message), [Strings.OK], 0);
        }
    }

    /// <summary>Shows the print preview window.</summary>
    public static void Preview(Window owner, SettingsService settings, PixelBuffer image, double dpiX, double dpiY, string jobName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var layout = new PrintLayout(image, dpiX, dpiY, DefaultPage, PageSettings.From(settings.Current));
        var w = new Window
        {
            Title = Strings.Dlg_PrintPreview,
            Width = 760,
            Height = 900,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(w, "PrintPreviewWindow");
        var index = 0;
        var pageImage = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(16) };
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(label, "PageLabel");
        var prev = new Button { Content = Strings.Dlg_Previous, MinWidth = 88 };
        var next = new Button { Content = Strings.Dlg_Next, MinWidth = 88 };
        var print = new Button { Content = Strings.Dlg_PrintButton, MinWidth = 88, Margin = new Thickness(12, 0, 0, 0) };
        var close = new Button { Content = Strings.Close, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        void Show()
        {
            pageImage.Source = new DrawingImage(layout.PageDrawing(index, paper: true));
            label.Text = string.Format(CultureInfo.CurrentCulture, Strings.Dlg_PageOf, index + 1, layout.PageCount);
            prev.IsEnabled = index > 0;
            next.IsEnabled = index < layout.PageCount - 1;
        }

        prev.Click += (_, _) =>
        {
            index--;
            Show();
        };
        next.Click += (_, _) =>
        {
            index++;
            Show();
        };
        print.Click += (_, _) =>
        {
            w.Close();
            Print(owner, settings, image, dpiX, dpiY, jobName);
        };
        close.Click += (_, _) => w.Close();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8) };
        bar.Children.Add(prev);
        bar.Children.Add(label);
        bar.Children.Add(next);
        bar.Children.Add(print);
        bar.Children.Add(close);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        var paper = new Border { Background = (Brush)w.FindResource("CanvasSurroundBrush"), Child = pageImage };
        System.Windows.Media.Effects.DropShadowEffect shadow = new() { BlurRadius = 12, Opacity = 0.35, ShadowDepth = 2 };
        pageImage.Effect = shadow;
        dock.Children.Add(paper);
        w.Content = dock;
        Show();
        w.ShowDialog();
    }
}
