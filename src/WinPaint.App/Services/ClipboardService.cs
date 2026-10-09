using System.Runtime.InteropServices;
using System.Windows;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.App.Services;

/// <summary>System clipboard access for images and text.</summary>
public interface IClipboardService
{
    /// <summary>Puts an image on the clipboard (PNG, DIB and bitmap formats).</summary>
    void SetImage(PixelBuffer image);

    /// <summary>Reads an image from the clipboard, or null.</summary>
    PixelBuffer? GetImage();
}

/// <summary>WPF clipboard implementation with retries for a busy clipboard.</summary>
public sealed class ClipboardService : IClipboardService
{
    /// <inheritdoc/>
    public void SetImage(PixelBuffer image) => Retry(() => Clipboard.SetDataObject(ClipboardData.Create(image), copy: true));

    /// <inheritdoc/>
    public PixelBuffer? GetImage()
    {
        PixelBuffer? result = null;
        Retry(() => result = ClipboardData.Read(Clipboard.GetDataObject()));
        return result;
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
            catch (ExternalException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }
}
