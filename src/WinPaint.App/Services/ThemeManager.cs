using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinPaint.App.Services;

/// <summary>
/// Applies the Fluent theme (Light/Dark/System) through <see cref="Application.ThemeMode"/> and swaps winPaint's own
/// brushes (canvas surround, rulers, handles) to match.
/// </summary>
public static class ThemeManager
{
    /// <summary>True when the effective theme is dark.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Applies a theme to the application.</summary>
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

#pragma warning disable WPF0001 // ThemeMode is marked experimental in .NET 10.
        app.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001

        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme(),
        };

        var r = app.Resources;
        Set(r, "CanvasSurroundBrush", IsDark ? 0x202020 : 0xE6EBF1);
        Set(r, "RulerBackgroundBrush", IsDark ? 0x2B2B2B : 0xF7F9FC);
        Set(r, "RulerForegroundBrush", IsDark ? 0x9A9A9A : 0x707070);
        Set(r, "HandleFillBrush", IsDark ? 0x1F1F1F : 0xFFFFFF);
        Set(r, "HandleStrokeBrush", IsDark ? 0xBFBFBF : 0x5A5A5A);
        Set(r, "HoverOutlineBrush", IsDark ? 0x60CDFF : 0x005FB8);
        Set(r, "AccentBrush", IsDark ? 0x60CDFF : 0x005FB8);
        Set(r, "ToolbarBackgroundBrush", IsDark ? 0x2C2C2C : 0xF9F9F9);
        Set(r, "ChromeBackgroundBrush", IsDark ? 0x202020 : 0xF3F3F3);
        Set(r, "PanelBackgroundBrush", IsDark ? 0x272727 : 0xFBFBFB);
        Set(r, "SeparatorBrush", IsDark ? 0x3D3D3D : 0xE0E0E0);
        Set(r, "SelectedToolBrush", IsDark ? 0x3A3A3A : 0xE1E9F5);
        Set(r, "SelectedToolBorderBrush", IsDark ? 0x60CDFF : 0x005FB8);
        Set(r, "HoverToolBrush", IsDark ? 0x333333 : 0xEDEDED);
        Set(r, "IconBrush", IsDark ? 0xE6E6E6 : 0x1B1B1B);
        Set(r, "SubtleTextBrush", IsDark ? 0xA0A0A0 : 0x616161);
        Set(r, "SwatchBorderBrush", IsDark ? 0x6E6E6E : 0x8A8A8A);
    }

    /// <summary>Reads the Windows app theme preference (AppsUseLightTheme).</summary>
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    private static void Set(ResourceDictionary r, string key, int rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        r[key] = b;
    }
}
