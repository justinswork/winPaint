using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPaint.Core.Imaging.Codecs;

/// <summary>A decoded image.</summary>
/// <param name="Pixels">Premultiplied pixels.</param>
/// <param name="DpiX">Horizontal resolution.</param>
/// <param name="DpiY">Vertical resolution.</param>
public sealed record DecodedImage(PixelBuffer Pixels, double DpiX, double DpiY);

/// <summary>Thrown when an image cannot be opened.</summary>
public sealed class ImageOpenException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ImageOpenException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public ImageOpenException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ImageOpenException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>Options for writing a file.</summary>
public sealed record EncodeOptions
{
    /// <summary>JPEG quality 1–100.</summary>
    public int JpegQuality { get; init; } = 90;

    /// <summary>Icon sizes to write (square edge lengths).</summary>
    public IReadOnlyList<int> IcoSizes { get; init; } = [16, 32, 48, 256];

    /// <summary>Resolution written to the file.</summary>
    public double DpiX { get; init; } = 96;

    /// <summary>Resolution written to the file.</summary>
    public double DpiY { get; init; } = 96;
}

/// <summary>Reads and writes image files.</summary>
public static class ImageCodec
{
    /// <summary>Decodes a file (first frame; largest frame for ICO).</summary>
    public static DecodedImage Decode(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Decode(fs);
        }
        catch (ImageOpenException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ImageOpenException($"Access to \"{Path.GetFileName(path)}\" was denied.", ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new ImageOpenException($"The file \"{Path.GetFileName(path)}\" could not be found.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new ImageOpenException($"The file \"{Path.GetFileName(path)}\" could not be found.", ex);
        }
        catch (IOException ex)
        {
            throw new ImageOpenException($"\"{Path.GetFileName(path)}\" could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>Decodes a stream.</summary>
    public static DecodedImage Decode(Stream stream)
    {
        try
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                throw new ImageOpenException("The file contains no image.");
            }

            BitmapFrame frame = decoder.Frames[0];
            if (decoder is IconBitmapDecoder)
            {
                frame = decoder.Frames.OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight).ThenByDescending(f => f.Format.BitsPerPixel).First();
            }

            var pixels = PixelBuffer.FromBitmapSource(frame);
            var dpiX = frame.DpiX > 1 ? frame.DpiX : 96;
            var dpiY = frame.DpiY > 1 ? frame.DpiY : 96;
            return new DecodedImage(pixels, Math.Round(dpiX, 2), Math.Round(dpiY, 2));
        }
        catch (ImageOpenException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            throw new ImageOpenException("This is not a valid image file, or its format is not supported.", ex);
        }
        catch (FileFormatException ex)
        {
            throw new ImageOpenException("The image file is damaged or in an unsupported format.", ex);
        }
        catch (ArgumentException ex)
        {
            throw new ImageOpenException("The image file is damaged or in an unsupported format.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new ImageOpenException("The image file is damaged or in an unsupported format.", ex);
        }
        catch (OverflowException ex)
        {
            throw new ImageOpenException("The image is too large to open.", ex);
        }
        catch (OutOfMemoryException ex)
        {
            throw new ImageOpenException("There is not enough memory to open this image.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new ImageOpenException("The image file is damaged or in an unsupported format.", ex);
        }
    }

    /// <summary>Encodes a flattened image to a file (written atomically via a temp file).</summary>
    public static void Encode(PixelBuffer image, string path, ImageFormat format, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            {
                Encode(image, fs, format, options);
            }

            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    /// <summary>Encodes a flattened image to a stream.</summary>
    public static void Encode(PixelBuffer image, Stream stream, ImageFormat format, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new EncodeOptions();
        switch (format)
        {
            case ImageFormat.Png:
                Save(new PngBitmapEncoder(), ToStraightBgra(image, options), stream);
                break;
            case ImageFormat.Jpeg:
                Save(new JpegBitmapEncoder { QualityLevel = Math.Clamp(options.JpegQuality, 1, 100) }, ToBgr24OnWhite(image, options), stream);
                break;
            case ImageFormat.Bmp:
                BmpEncoder.Write(image, stream, options.DpiX, options.DpiY);
                break;
            case ImageFormat.Tiff:
                Save(new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw }, ToStraightBgra(image, options), stream);
                break;
            case ImageFormat.Gif:
                GifEncoder.Write(image, stream);
                break;
            case ImageFormat.Ico:
                IcoEncoder.Write(image, stream, options.IcoSizes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    /// <summary>Encodes to PNG bytes (used for the clipboard).</summary>
    public static byte[] EncodePng(PixelBuffer image)
    {
        using var ms = new MemoryStream();
        Encode(image, ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Straight-alpha BGRA bitmap source.</summary>
    public static BitmapSource ToStraightBgra(PixelBuffer image, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var straight = new uint[image.Pixels.Length];
        for (var i = 0; i < straight.Length; i++)
        {
            straight[i] = ColorUtil.Unpremultiply(image.Pixels[i]);
        }

        var bmp = BitmapSource.Create(image.Width, image.Height, options?.DpiX ?? 96, options?.DpiY ?? 96, PixelFormats.Bgra32, null, straight, image.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Composites onto white and returns a 24-bit BGR bitmap.</summary>
    public static BitmapSource ToBgr24OnWhite(PixelBuffer image, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var stride = ((image.Width * 3) + 3) & ~3;
        var bytes = new byte[stride * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var p = ColorUtil.Over(image[x, y], ColorUtil.White);
                var o = (y * stride) + (x * 3);
                bytes[o] = ColorUtil.B(p);
                bytes[o + 1] = ColorUtil.G(p);
                bytes[o + 2] = ColorUtil.R(p);
            }
        }

        var bmp = BitmapSource.Create(image.Width, image.Height, options?.DpiX ?? 96, options?.DpiY ?? 96, PixelFormats.Bgr24, null, bytes, stride);
        bmp.Freeze();
        return bmp;
    }

    private static void Save(BitmapEncoder encoder, BitmapSource source, Stream stream)
    {
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }
}
