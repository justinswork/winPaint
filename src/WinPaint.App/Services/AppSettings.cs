using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinPaint.App.Services;

/// <summary>Theme choice.</summary>
public enum AppTheme
{
    /// <summary>Follow Windows.</summary>
    System,

    /// <summary>Light.</summary>
    Light,

    /// <summary>Dark.</summary>
    Dark,
}

/// <summary>Persisted user preferences (%AppData%\winPaint\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>Window left.</summary>
    public double? WindowLeft { get; set; }

    /// <summary>Window top.</summary>
    public double? WindowTop { get; set; }

    /// <summary>Window width.</summary>
    public double WindowWidth { get; set; } = 1280;

    /// <summary>Window height.</summary>
    public double WindowHeight { get; set; } = 820;

    /// <summary>Window maximized.</summary>
    public bool WindowMaximized { get; set; }

    /// <summary>Theme.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Rulers visible.</summary>
    public bool ShowRulers { get; set; }

    /// <summary>Gridlines visible.</summary>
    public bool ShowGridlines { get; set; }

    /// <summary>Status bar visible.</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>Layers panel visible.</summary>
    public bool ShowLayers { get; set; }

    /// <summary>Last tool.</summary>
    public string Tool { get; set; } = "Pencil";

    /// <summary>Last brush.</summary>
    public string Brush { get; set; } = "Brush";

    /// <summary>Last shape.</summary>
    public string Shape { get; set; } = "Rectangle";

    /// <summary>Shape outline style.</summary>
    public string Outline { get; set; } = "Solid";

    /// <summary>Shape fill style.</summary>
    public string Fill { get; set; } = "None";

    /// <summary>Pencil size.</summary>
    public int PencilSize { get; set; } = 1;

    /// <summary>Brush size.</summary>
    public int BrushSize { get; set; } = 8;

    /// <summary>Eraser size.</summary>
    public int EraserSize { get; set; } = 8;

    /// <summary>Shape outline size.</summary>
    public int ShapeSize { get; set; } = 2;

    /// <summary>Opacity (0.01–1).</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Color 1 (#AARRGGBB).</summary>
    public string Primary { get; set; } = "#FF000000";

    /// <summary>Color 2 (#AARRGGBB).</summary>
    public string Secondary { get; set; } = "#FFFFFFFF";

    /// <summary>Custom colors (#AARRGGBB, empty = unused slot).</summary>
    public List<string> CustomColors { get; set; } = [];

    /// <summary>Recent files, most recent first.</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Last canvas size used by New.</summary>
    public int NewWidth { get; set; } = 1152;

    /// <summary>Last canvas size used by New.</summary>
    public int NewHeight { get; set; } = 648;

    /// <summary>Text font family.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Text font size (pt).</summary>
    public double FontSizePt { get; set; } = 11;

    /// <summary>Transparent selection toggle.</summary>
    public bool TransparentSelection { get; set; }

    /// <summary>Undo memory budget (MB).</summary>
    public int UndoBudgetMb { get; set; } = 1024;

    /// <summary>Page setup: landscape.</summary>
    public bool PrintLandscape { get; set; }

    /// <summary>Page setup margins (inches): left, top, right, bottom.</summary>
    public double[] PrintMargins { get; set; } = [0.75, 0.75, 0.75, 0.75];

    /// <summary>Page setup: scale percent (when not fitting).</summary>
    public int PrintScalePercent { get; set; } = 100;

    /// <summary>Page setup: fit to pages (0 = use scale).</summary>
    public int PrintFitWide { get; set; }

    /// <summary>Page setup: fit to pages tall.</summary>
    public int PrintFitTall { get; set; }

    /// <summary>Page setup: center horizontally.</summary>
    public bool PrintCenterH { get; set; } = true;

    /// <summary>Page setup: center vertically.</summary>
    public bool PrintCenterV { get; set; }
}

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Creates the service for a settings file path (default: %AppData%\winPaint\settings.json).</summary>
    public SettingsService(string? path = null)
    {
        FilePath = path ?? Environment.GetEnvironmentVariable("WINPAINT_SETTINGS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "winPaint", "settings.json");
    }

    /// <summary>Settings file location.</summary>
    public string FilePath { get; }

    /// <summary>Current settings.</summary>
    public AppSettings Current { get; private set; } = new();

    /// <summary>Loads settings (defaults when missing or unreadable).</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (JsonException)
        {
            Current = new AppSettings();
        }
        catch (IOException)
        {
            Current = new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            Current = new AppSettings();
        }

        return Current;
    }

    /// <summary>Saves settings; failures are ignored (settings are a convenience).</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Options));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
