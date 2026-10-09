using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.App.Views;

/// <summary>Helpers to build simple dialogs in code.</summary>
internal static class DialogKit
{
    public static Window Create(string title, string automationId, double width)
    {
        var w = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            Width = width,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(w, automationId);
        return w;
    }

    public static StackPanel Buttons(Window owner, bool cancel, Action? ok = null, string? okText = null)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var okButton = new Button { Content = okText ?? Strings.OK, IsDefault = true, MinWidth = 88 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(okButton, "OkButton");
        okButton.Click += (_, _) =>
        {
            ok?.Invoke();
            if (cancel)
            {
                owner.DialogResult = true;
            }
            else
            {
                owner.Close();
            }
        };
        bar.Children.Add(okButton);
        if (cancel)
        {
            var c = new Button { Content = Strings.Cancel, IsCancel = true, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(c, "CancelButton");
            bar.Children.Add(c);
        }
        else
        {
            okButton.IsCancel = true;
        }

        return bar;
    }

    public static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };

    public static TextBox NumberBox(double value, string id)
    {
        var tb = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Width = 70, Margin = new Thickness(4, 2, 12, 2) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(tb, id);
        return tb;
    }

    public static double Parse(TextBox tb, double fallback) =>
        double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : fallback;
}

/// <summary>The keyboard shortcuts table (F1).</summary>
public static class ShortcutsDialog
{
    /// <summary>All shortcuts (§5.12).</summary>
    public static IReadOnlyList<(string Keys, string Action)> Table { get; } =
    [
        ("Ctrl+N", Strings.Sc_New), ("Ctrl+O", Strings.Sc_Open), ("Ctrl+S", Strings.Sc_Save), ("F12 / Ctrl+Shift+S", Strings.Sc_SaveAs),
        ("Ctrl+P", Strings.Sc_Print), ("Ctrl+E", Strings.Sc_Properties), ("Ctrl+Z", Strings.Sc_Undo), ("Ctrl+Y / Ctrl+Shift+Z", Strings.Sc_Redo),
        ("Ctrl+X / C / V", Strings.Sc_CutCopyPaste), ("Ctrl+A", Strings.Sc_SelectAll), ("Del", Strings.Sc_Delete), ("Esc", Strings.Sc_Escape),
        ("Ctrl+W", Strings.Sc_ResizeSkew), ("Ctrl+Shift+X", Strings.Sc_Crop), ("Ctrl+Shift+I", Strings.Sc_Invert), ("Ctrl+R", Strings.Sc_Rulers),
        ("Ctrl+G", Strings.Sc_Gridlines), ("F11", Strings.Sc_FullScreen), ("Ctrl+PgUp / PgDn", Strings.Sc_ZoomInOut), ("Ctrl+wheel", Strings.Sc_ZoomWheel),
        ("Ctrl+Plus / Minus", Strings.Sc_SizeUpDown), ("X", Strings.Sc_Swap), ("Arrows / Shift+Arrows", Strings.Sc_Nudge), ("Ctrl+B / I / U", Strings.Sc_TextFormat),
        ("Ctrl+0", Strings.Sc_Fit), ("Ctrl+1", Strings.Sc_Actual), ("Enter", Strings.Sc_NewLine), ("Tab / Shift+Tab", Strings.Sc_TabNav), ("F1", Strings.Sc_Help),
    ];

    /// <summary>Shows the dialog.</summary>
    public static void Show(Window owner)
    {
        var w = DialogKit.Create(Strings.Dlg_Shortcuts, "ShortcutsDialog", 520);
        w.Owner = owner;
        var grid = new DataGrid
        {
            ItemsSource = Table.Select(t => new { Shortcut = t.Keys, Action = t.Action }).ToList(),
            IsReadOnly = true,
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Height = 460,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            CanUserSortColumns = false,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = Strings.Dlg_Shortcut, Binding = new System.Windows.Data.Binding("Shortcut"), Width = 170 });
        grid.Columns.Add(new DataGridTextColumn { Header = Strings.Dlg_Action, Binding = new System.Windows.Data.Binding("Action"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        System.Windows.Automation.AutomationProperties.SetAutomationId(grid, "ShortcutTable");
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(grid);
        root.Children.Add(DialogKit.Buttons(w, cancel: false, okText: Strings.Close));
        w.Content = root;
        w.ShowDialog();
    }
}

/// <summary>Icon sizes picker for ICO saving.</summary>
public static class IcoSizesDialog
{
    /// <summary>Shows the dialog; returns the chosen sizes or null.</summary>
    public static IReadOnlyList<int>? Show(Window owner)
    {
        var w = DialogKit.Create(Strings.Dlg_IcoSizes, "IcoSizesDialog", 320);
        w.Owner = owner;
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = Strings.Dlg_IcoSizesPrompt, TextWrapping = TextWrapping.Wrap });
        var boxes = new List<(int Size, CheckBox Box)>();
        foreach (var s in IcoEncoder.AvailableSizes)
        {
            var cb = new CheckBox { Content = $"{s} × {s}", IsChecked = s is 16 or 32 or 48 or 256, Margin = new Thickness(0, 4, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(cb, $"Ico{s}");
            boxes.Add((s, cb));
            root.Children.Add(cb);
        }

        root.Children.Add(DialogKit.Buttons(w, cancel: true));
        w.Content = root;
        if (w.ShowDialog() != true)
        {
            return null;
        }

        var sizes = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Size).ToList();
        return sizes.Count == 0 ? [256] : sizes;
    }
}

/// <summary>Settings: theme, undo budget and About.</summary>
public static class SettingsDialog
{
    /// <summary>Shows the dialog.</summary>
    public static void Show(Window owner, SettingsService settings, Action<AppTheme> applyTheme)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(applyTheme);
        var w = DialogKit.Create(Strings.Dlg_SettingsTitle, "SettingsDialog", 420);
        w.Owner = owner;
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = Strings.Dlg_SettingsTitle, FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(DialogKit.Heading(Strings.Dlg_Theme));
        var current = settings.Current.Theme;
        foreach (var (theme, label) in new[] { (AppTheme.Light, Strings.Theme_Light), (AppTheme.Dark, Strings.Theme_Dark), (AppTheme.System, Strings.Theme_System) })
        {
            var rb = new RadioButton { Content = label, GroupName = "theme", IsChecked = current == theme, Margin = new Thickness(0, 2, 0, 2) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(rb, "Theme" + theme);
            rb.Checked += (_, _) => applyTheme(theme);
            root.Children.Add(rb);
        }

        root.Children.Add(DialogKit.Heading(Strings.Dlg_UndoBudget));
        var budget = DialogKit.NumberBox(settings.Current.UndoBudgetMb, "UndoBudget");
        budget.HorizontalAlignment = HorizontalAlignment.Left;
        budget.Width = 100;
        root.Children.Add(budget);

        root.Children.Add(DialogKit.Heading(Strings.Dlg_About));
        var version = typeof(SettingsDialog).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        root.Children.Add(new TextBlock { Text = Strings.AppName, FontSize = 16, FontWeight = FontWeights.SemiBold });
        root.Children.Add(Labeled(string.Format(CultureInfo.CurrentCulture, Strings.Dlg_Version, version), "AboutVersion"));
        root.Children.Add(Labeled(string.Format(CultureInfo.CurrentCulture, Strings.Dlg_DotNet, Environment.Version), "AboutDotNet"));
        root.Children.Add(new TextBlock { Text = Strings.Dlg_AboutText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)w.FindResource("SubtleTextBrush") });
        root.Children.Add(DialogKit.Buttons(w, cancel: false, ok: () =>
        {
            settings.Current.UndoBudgetMb = (int)Math.Clamp(DialogKit.Parse(budget, settings.Current.UndoBudgetMb), 64, 65536);
            settings.Save();
        }));
        w.Content = root;
        w.ShowDialog();

        static TextBlock Labeled(string text, string id)
        {
            var t = new TextBlock { Text = text };
            System.Windows.Automation.AutomationProperties.SetAutomationId(t, id);
            return t;
        }
    }
}
