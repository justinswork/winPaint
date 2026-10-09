using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPaint.UiTests;

/// <summary>Straight-alpha BGRA image loaded from a PNG for assertions.</summary>
public sealed class ImageCheck
{
    private ImageCheck(int width, int height, uint[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width.</summary>
    public int Width { get; }

    /// <summary>Height.</summary>
    public int Height { get; }

    /// <summary>Pixels (B | G &lt;&lt; 8 | R &lt;&lt; 16 | A &lt;&lt; 24).</summary>
    public uint[] Pixels { get; }

    /// <summary>Pixel accessor.</summary>
    public uint this[int x, int y] => Pixels[(y * Width) + x];

    /// <summary>Loads a PNG (retrying while the file is still being written).</summary>
    public static ImageCheck Load(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var conv = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
                var px = new uint[conv.PixelWidth * conv.PixelHeight];
                conv.CopyPixels(px, conv.PixelWidth * 4, 0);
                return new ImageCheck(conv.PixelWidth, conv.PixelHeight, px);
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Counts pixels in a rectangle that are not (nearly) white.</summary>
    public int Ink(int x, int y, int w, int h)
    {
        var n = 0;
        for (var yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
        {
            for (var xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
            {
                var p = this[xx, yy];
                if ((p >> 24) > 0 && (((p >> 16) & 0xFF) < 235 || ((p >> 8) & 0xFF) < 235 || (p & 0xFF) < 235))
                {
                    n++;
                }
            }
        }

        return n;
    }

    /// <summary>Counts pixels with a dominant red.</summary>
    public int Red(int x, int y, int w, int h)
    {
        var n = 0;
        for (var yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
        {
            for (var xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
            {
                var p = this[xx, yy];
                if (((p >> 16) & 0xFF) > 180 && ((p >> 8) & 0xFF) < 90 && (p & 0xFF) < 90)
                {
                    n++;
                }
            }
        }

        return n;
    }

    /// <summary>Number of differing pixels between two images in a rectangle.</summary>
    public static int Diff(ImageCheck a, ImageCheck b, int x, int y, int w, int h)
    {
        var n = 0;
        for (var yy = Math.Max(0, y); yy < Math.Min(Math.Min(a.Height, b.Height), y + h); yy++)
        {
            for (var xx = Math.Max(0, x); xx < Math.Min(Math.Min(a.Width, b.Width), x + w); xx++)
            {
                if (a[xx, yy] != b[xx, yy])
                {
                    n++;
                }
            }
        }

        return n;
    }
}
