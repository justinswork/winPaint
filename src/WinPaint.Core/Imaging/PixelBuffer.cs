using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPaint.Core.Imaging;

/// <summary>A dense premultiplied BGRA32 pixel buffer.</summary>
public sealed class PixelBuffer
{
    /// <summary>Creates a buffer filled with <paramref name="fill"/>.</summary>
    public PixelBuffer(int width, int height, uint fill = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
        Pixels = new uint[(long)width * height];
        if (fill != 0)
        {
            Array.Fill(Pixels, fill);
        }
    }

    /// <summary>Wraps an existing pixel array (not copied).</summary>
    public PixelBuffer(int width, int height, uint[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.LongLength != (long)width * height)
        {
            throw new ArgumentException("Pixel array size mismatch.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Row-major pixel storage.</summary>
    public uint[] Pixels { get; }

    /// <summary>Full bounds.</summary>
    public PixelRect Bounds => new(0, 0, Width, Height);

    /// <summary>Pixel accessor (no bounds check beyond the array's).</summary>
    public uint this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    /// <summary>Returns the pixel or 0 when out of range.</summary>
    public uint GetOrDefault(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Pixels[(y * Width) + x] : 0;

    /// <summary>Deep copy.</summary>
    public PixelBuffer Clone() => new(Width, Height, (uint[])Pixels.Clone());

    /// <summary>Copies a sub-rectangle into a new buffer (areas outside are transparent).</summary>
    public PixelBuffer Crop(PixelRect r)
    {
        var result = new PixelBuffer(r.Width, r.Height);
        var src = r.Intersect(Bounds);
        for (var y = src.Y; y < src.Bottom; y++)
        {
            Array.Copy(Pixels, (y * Width) + src.X, result.Pixels, ((y - r.Y) * r.Width) + (src.X - r.X), src.Width);
        }

        return result;
    }

    /// <summary>Fills a rectangle with a pixel value.</summary>
    public void Fill(PixelRect r, uint value)
    {
        r = r.Intersect(Bounds);
        for (var y = r.Y; y < r.Bottom; y++)
        {
            Pixels.AsSpan((y * Width) + r.X, r.Width).Fill(value);
        }
    }

    /// <summary>Draws <paramref name="src"/> at (dx,dy) using source-over.</summary>
    public void DrawOver(PixelBuffer src, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(src);
        var r = new PixelRect(dx, dy, src.Width, src.Height).Intersect(Bounds);
        for (var y = r.Y; y < r.Bottom; y++)
        {
            var srow = ((y - dy) * src.Width) - dx;
            var drow = y * Width;
            for (var x = r.X; x < r.Right; x++)
            {
                Pixels[drow + x] = ColorUtil.Over(src.Pixels[srow + x], Pixels[drow + x]);
            }
        }
    }

    /// <summary>Copies <paramref name="src"/> at (dx,dy), replacing pixels.</summary>
    public void Blit(PixelBuffer src, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(src);
        var r = new PixelRect(dx, dy, src.Width, src.Height).Intersect(Bounds);
        for (var y = r.Y; y < r.Bottom; y++)
        {
            Array.Copy(src.Pixels, ((y - dy) * src.Width) + (r.X - dx), Pixels, (y * Width) + r.X, r.Width);
        }
    }

    /// <summary>True when any pixel has alpha below 255.</summary>
    public bool HasTransparency()
    {
        foreach (var p in Pixels)
        {
            if (p < 0xFF000000)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Creates a frozen WPF bitmap (Pbgra32) from the buffer.</summary>
    public BitmapSource ToBitmapSource(double dpiX = 96, double dpiY = 96)
    {
        var bmp = BitmapSource.Create(Width, Height, dpiX, dpiY, PixelFormats.Pbgra32, null, Pixels, Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Creates a buffer from any WPF bitmap source.</summary>
    public static PixelBuffer FromBitmapSource(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        BitmapSource conv = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var buf = new PixelBuffer(conv.PixelWidth, conv.PixelHeight);
        conv.CopyPixels(buf.Pixels, buf.Width * 4, 0);
        return buf;
    }

    /// <summary>Byte-level equality.</summary>
    public bool ContentEquals(PixelBuffer? other) =>
        other is not null && other.Width == Width && other.Height == Height && Pixels.AsSpan().SequenceEqual(other.Pixels);

    /// <summary>Stable hash of the pixel content (SHA-256 hex).</summary>
    public string ContentHash()
    {
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(Pixels.AsSpan());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return $"{Width}x{Height}:{Convert.ToHexString(hash)}";
    }
}
