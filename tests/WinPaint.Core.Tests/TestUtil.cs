using System.Runtime.ExceptionServices;
using System.Windows.Media;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Tests;

/// <summary>Shared helpers for Core tests.</summary>
internal static class TestUtil
{
    /// <summary>Runs an action on a fresh STA thread (WPF imaging/text APIs need STA).</summary>
    public static void Sta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Throw(error);
        }
    }

    /// <summary>Premultiplied opaque pixel from RGB.</summary>
    public static uint Rgb(byte r, byte g, byte b) => ColorUtil.Pack(255, r, g, b);

    /// <summary>Deterministic pseudo-random image.</summary>
    public static PixelBuffer RandomImage(int w, int h, int seed, bool opaque = true)
    {
        var rng = new Random(seed);
        var b = new PixelBuffer(w, h);
        for (var i = 0; i < b.Pixels.Length; i++)
        {
            var a = opaque ? (byte)255 : (byte)rng.Next(256);
            b.Pixels[i] = ColorUtil.Premultiply(a, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
        }

        return b;
    }

    /// <summary>Temp file path in a per-test directory.</summary>
    public static string TempFile(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winPaintTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>Opaque color.</summary>
    public static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
