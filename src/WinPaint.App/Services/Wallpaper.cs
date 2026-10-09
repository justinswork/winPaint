using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinPaint.App.Services;

/// <summary>Desktop background placement.</summary>
public enum WallpaperStyle
{
    /// <summary>Fill the screen.</summary>
    Fill,

    /// <summary>Tile.</summary>
    Tile,

    /// <summary>Center.</summary>
    Center,
}

/// <summary>Sets the desktop background (SystemParametersInfo + registry style values).</summary>
public static partial class Wallpaper
{
    private const int SpiSetDeskWallpaper = 0x0014;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendChange = 0x02;

    /// <summary>Sets an image file as the wallpaper. Returns false on failure.</summary>
    public static bool Set(string path, WallpaperStyle style)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
            {
                if (key is null)
                {
                    return false;
                }

                var (wallpaperStyle, tile) = RegistryValues(style);
                key.SetValue("WallpaperStyle", wallpaperStyle);
                key.SetValue("TileWallpaper", tile);
            }

            return SystemParametersInfo(SpiSetDeskWallpaper, 0, path, SpifUpdateIniFile | SpifSendChange);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Registry values (WallpaperStyle, TileWallpaper) for a placement.</summary>
    public static (string WallpaperStyle, string TileWallpaper) RegistryValues(WallpaperStyle style) => style switch
    {
        WallpaperStyle.Tile => ("0", "1"),
        WallpaperStyle.Center => ("0", "0"),
        _ => ("10", "0"),
    };

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(int action, int param, string vparam, int winIni);
}
