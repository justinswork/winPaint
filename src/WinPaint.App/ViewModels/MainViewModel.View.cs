using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPaint.App.Services;
using WinPaint.Core.Tools;
using WinPaint.Core.View;

namespace WinPaint.App.ViewModels;

/// <summary>View menu: zoom, rulers, gridlines, status bar, layers panel, full screen, thumbnail, theme.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Raised when the theme changes.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Zoom (1 = 100 %).</summary>
    [ObservableProperty]
    public partial double Zoom { get; set; } = 1;

    /// <summary>Rulers visible.</summary>
    [ObservableProperty]
    public partial bool ShowRulers { get; set; }

    /// <summary>Gridlines visible.</summary>
    [ObservableProperty]
    public partial bool ShowGridlines { get; set; }

    /// <summary>Status bar visible.</summary>
    [ObservableProperty]
    public partial bool ShowStatusBar { get; set; } = true;

    /// <summary>Layers panel visible.</summary>
    [ObservableProperty]
    public partial bool ShowLayers { get; set; }

    /// <summary>Thumbnail window visible.</summary>
    [ObservableProperty]
    public partial bool ShowThumbnail { get; set; }

    /// <summary>Theme.</summary>
    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    /// <summary>Zoom as a percentage (status bar slider/box).</summary>
    public double ZoomPercent
    {
        get => Math.Round(Zoom * 100, 1);
        set => Zoom = Math.Clamp(value, 1, 800) / 100.0;
    }

    [RelayCommand]
    private void ZoomIn() => Zoom = ViewTransform.NextPreset(Zoom);

    [RelayCommand]
    private void ZoomOut() => Zoom = ViewTransform.PreviousPreset(Zoom);

    [RelayCommand]
    private void ZoomActual() => Zoom = 1;

    [RelayCommand]
    private void ZoomFit() => View?.ZoomToFit();

    [RelayCommand]
    private void SetZoomPreset(double zoom) => Zoom = zoom;

    [RelayCommand]
    private void ToggleRulers() => ShowRulers = !ShowRulers;

    [RelayCommand]
    private void ToggleGridlines() => ShowGridlines = !ShowGridlines;

    [RelayCommand]
    private void ToggleStatusBar() => ShowStatusBar = !ShowStatusBar;

    [RelayCommand]
    private void ToggleLayers() => ShowLayers = !ShowLayers;

    [RelayCommand]
    private void ToggleThumbnail() => ShowThumbnail = !ShowThumbnail;

    [RelayCommand]
    private void FullScreen()
    {
        PrepareForCommand();
        View?.ShowFullScreen();
    }

    [RelayCommand]
    private void SetTheme(AppTheme theme) => Theme = theme;

    [RelayCommand]
    private void ShowShortcuts() => _dialogs.ShowShortcuts();

    [RelayCommand]
    private void ShowSettingsDialog() => _dialogs.ShowSettings();

    /// <summary>Ctrl+Plus / Ctrl+Minus: size for size-adjustable tools, zoom otherwise (Paint behavior).</summary>
    public void SizeOrZoom(bool increase)
    {
        if (ToolSettings.HasSize(ActiveToolKind) && Text.Session is null)
        {
            AdjustSize(increase ? 1 : -1);
        }
        else if (increase)
        {
            ZoomIn();
        }
        else
        {
            ZoomOut();
        }
    }

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomPercent));
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnShowRulersChanged(bool value) => SaveSettingsIfReady();

    partial void OnShowGridlinesChanged(bool value) => SaveSettingsIfReady();

    partial void OnShowStatusBarChanged(bool value) => SaveSettingsIfReady();

    partial void OnShowLayersChanged(bool value) => SaveSettingsIfReady();

    partial void OnShowThumbnailChanged(bool value) => View?.ShowThumbnail(value);

    partial void OnThemeChanged(AppTheme value)
    {
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        SaveSettingsIfReady();
    }

    private void SaveSettingsIfReady()
    {
        if (_tools.Count > 0)
        {
            SaveSettings();
        }
    }
}
