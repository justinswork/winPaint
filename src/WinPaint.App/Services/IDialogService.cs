using System.Windows.Media;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.App.Services;

/// <summary>Answer to the unsaved-changes prompt.</summary>
public enum SaveChoice
{
    /// <summary>Save first.</summary>
    Save,

    /// <summary>Discard changes.</summary>
    DontSave,

    /// <summary>Abort the operation.</summary>
    Cancel,
}

/// <summary>Resize and skew dialog values.</summary>
/// <param name="Width">New width in pixels.</param>
/// <param name="Height">New height in pixels.</param>
/// <param name="SkewHorizontal">Horizontal skew in degrees.</param>
/// <param name="SkewVertical">Vertical skew in degrees.</param>
public sealed record ResizeSkewResult(int Width, int Height, double SkewHorizontal, double SkewVertical);

/// <summary>Where and how to save.</summary>
/// <param name="Path">Full path.</param>
/// <param name="Format">Image format (ignored for a project).</param>
/// <param name="IsProject">True for a winPaint project (.wpp).</param>
public sealed record SaveTarget(string Path, ImageFormat Format, bool IsProject);

/// <summary>Image properties dialog input.</summary>
public sealed record ImagePropertiesInfo(int Width, int Height, double DpiX, double DpiY, DateTime? LastSaved, long? SizeOnDisk);

/// <summary>Image properties dialog result.</summary>
/// <param name="Width">Canvas width (px).</param>
/// <param name="Height">Canvas height (px).</param>
/// <param name="BlackAndWhite">Convert to black and white.</param>
public sealed record ImagePropertiesResult(int Width, int Height, bool BlackAndWhite);

/// <summary>Modal UI the view model asks for. Implemented by the WPF layer; replaceable in tests.</summary>
public interface IDialogService
{
    /// <summary>Shows the open-file dialog.</summary>
    string? PickOpenFile();

    /// <summary>
    /// Save dialog. <paramref name="project"/> preselects the winPaint project type; <paramref name="allowProject"/>
    /// offers it at all.
    /// </summary>
    SaveTarget? PickSaveFile(string suggestedName, ImageFormat format, bool project, bool allowProject);

    /// <summary>Asks whether to save changes.</summary>
    SaveChoice AskSaveChanges(string documentName);

    /// <summary>OK/Cancel confirmation.</summary>
    bool Confirm(string message);

    /// <summary>Error message.</summary>
    void ShowError(string message);

    /// <summary>Information message.</summary>
    void Info(string message);

    /// <summary>Resize and skew dialog.</summary>
    ResizeSkewResult? ResizeSkew(int width, int height);

    /// <summary>Image properties dialog.</summary>
    ImagePropertiesResult? ImageProperties(ImagePropertiesInfo info);

    /// <summary>Edit colors dialog. Returns the chosen color, or null when cancelled.</summary>
    Color? EditColors(Color initial, Action<Color> addCustom);

    /// <summary>Icon sizes picker for ICO saving.</summary>
    IReadOnlyList<int>? PickIcoSizes();

    /// <summary>Keyboard shortcuts help.</summary>
    void ShowShortcuts();

    /// <summary>Settings dialog (theme, undo budget, about).</summary>
    void ShowSettings();

    /// <summary>Page setup dialog. Returns true when accepted.</summary>
    bool PageSetup();

    /// <summary>Print with the system dialog.</summary>
    void Print(Core.Imaging.PixelBuffer image, double dpiX, double dpiY, string jobName);

    /// <summary>Print preview window.</summary>
    void PrintPreview(Core.Imaging.PixelBuffer image, double dpiX, double dpiY, string jobName);
}
