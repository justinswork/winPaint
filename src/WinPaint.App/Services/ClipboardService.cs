using System.Runtime.InteropServices;
using System.Windows;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.App.Services;

/// <summary>System clipboard access for images.</summary>
public interface IClipboardService
{
    /// <summary>True when the last operation could not use the system clipboard (an in-app copy was used).</summary>
    bool LastUsedFallback { get; }

    /// <summary>Puts an image on the clipboard (PNG, DIB and bitmap formats).</summary>
    void SetImage(PixelBuffer image);

    /// <summary>Reads an image from the clipboard, or null.</summary>
    PixelBuffer? GetImage();
}

/// <summary>
/// WPF clipboard implementation with retries for a busy clipboard. When the system clipboard can't be opened at all
/// (for example on a locked or restricted session) the image is kept in an in-process clipboard so copy and paste
/// still work inside winPaint.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private PixelBuffer? _fallback;
    private bool _fallbackIsNewest;

    /// <inheritdoc/>
    public bool LastUsedFallback { get; private set; }

    /// <inheritdoc/>
    public void SetImage(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _fallback = image.Clone();
        LastUsedFallback = !TryRetry(() => Clipboard.SetDataObject(ClipboardData.Create(image), copy: true));
        _fallbackIsNewest = LastUsedFallback;
    }

    /// <inheritdoc/>
    public PixelBuffer? GetImage()
    {
        PixelBuffer? result = null;
        LastUsedFallback = !TryRetry(() => result = ClipboardData.Read(Clipboard.GetDataObject()));
        if (LastUsedFallback || (_fallbackIsNewest && result is null))
        {
            LastUsedFallback = true;
            return _fallback?.Clone();
        }

        return result;
    }

    private static bool TryRetry(Action action)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }
}
