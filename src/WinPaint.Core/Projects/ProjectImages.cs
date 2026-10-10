using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Projects;

/// <summary>PNG encoding of layer pixels and erase masks, colors as text, and image fingerprints.</summary>
internal static class ProjectImages
{
    /// <summary>Straight-alpha RGBA PNG of premultiplied pixels.</summary>
    public static byte[] EncodeColorPng(PixelBuffer premultiplied)
    {
        var straight = new uint[premultiplied.Pixels.Length];
        for (var i = 0; i < straight.Length; i++)
        {
            straight[i] = ColorUtil.Unpremultiply(premultiplied.Pixels[i]);
        }

        var bmp = BitmapSource.Create(premultiplied.Width, premultiplied.Height, 96, 96, PixelFormats.Bgra32, null, straight, premultiplied.Width * 4);
        return Encode(bmp);
    }

    /// <summary>8-bit grayscale PNG of an erase mask (the low byte of each value).</summary>
    public static byte[] EncodeMaskPng(PixelBuffer mask)
    {
        var gray = new byte[mask.Pixels.Length];
        for (var i = 0; i < gray.Length; i++)
        {
            gray[i] = (byte)mask.Pixels[i];
        }

        var bmp = BitmapSource.Create(mask.Width, mask.Height, 96, 96, PixelFormats.Gray8, null, gray, mask.Width);
        return Encode(bmp);
    }

    /// <summary>Decodes a color PNG to premultiplied pixels; the size must match.</summary>
    public static PixelBuffer DecodeColorPng(byte[] png, int width, int height)
    {
        var frame = DecodeFrame(png, width, height);
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var buf = new PixelBuffer(width, height);
        conv.CopyPixels(buf.Pixels, width * 4, 0);
        for (var i = 0; i < buf.Pixels.Length; i++)
        {
            var p = buf.Pixels[i];
            buf.Pixels[i] = ColorUtil.Premultiply(ColorUtil.A(p), ColorUtil.R(p), ColorUtil.G(p), ColorUtil.B(p));
        }

        return buf;
    }

    /// <summary>Decodes a grayscale mask PNG; the size must match.</summary>
    public static PixelBuffer DecodeMaskPng(byte[] png, int width, int height)
    {
        var frame = DecodeFrame(png, width, height);
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
        var gray = new byte[width * height];
        conv.CopyPixels(gray, width, 0);
        var buf = new PixelBuffer(width, height);
        for (var i = 0; i < gray.Length; i++)
        {
            buf.Pixels[i] = gray[i];
        }

        return buf;
    }

    /// <summary><c>#AARRGGBB</c> of a premultiplied value.</summary>
    public static string ToHex(uint premultiplied) => ToHex(ColorUtil.ToColor(premultiplied));

    /// <summary><c>#AARRGGBB</c>.</summary>
    public static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Parses <c>#AARRGGBB</c>.</summary>
    public static Color ParseHex(string? s)
    {
        if (s is null || s.Length != 9 || s[0] != '#'
            || !uint.TryParse(s.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
        {
            throw new ProjectFormatException($"Invalid color \"{s}\".");
        }

        return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>Lowercase hex SHA-256.</summary>
    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>
    /// Fingerprint of an image as it decodes (WPP spec §6.5): SHA-256 of width and height (32-bit big-endian),
    /// then straight-alpha RGBA bytes row by row. Lowercase hex.
    /// </summary>
    public static string Fingerprint(PixelBuffer premultiplied)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> head = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head, premultiplied.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head[4..], premultiplied.Height);
        hash.AppendData(head);
        var row = new byte[premultiplied.Width * 4];
        for (var y = 0; y < premultiplied.Height; y++)
        {
            var src = premultiplied.Pixels.AsSpan(y * premultiplied.Width, premultiplied.Width);
            for (var x = 0; x < src.Length; x++)
            {
                var p = ColorUtil.Unpremultiply(src[x]);
                row[(x * 4) + 0] = ColorUtil.R(p);
                row[(x * 4) + 1] = ColorUtil.G(p);
                row[(x * 4) + 2] = ColorUtil.B(p);
                row[(x * 4) + 3] = ColorUtil.A(p);
            }

            hash.AppendData(row);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static BitmapFrame DecodeFrame(byte[] png, int width, int height)
    {
        // OnDemand: the header is read first, so a part claiming a different (e.g. huge) size is rejected before
        // any pixels are allocated.
        var decoder = new PngBitmapDecoder(new MemoryStream(png, writable: false), BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count != 1)
        {
            throw new ProjectFormatException("A layer image has an unexpected number of frames.");
        }

        var frame = decoder.Frames[0];
        if (frame.PixelWidth != width || frame.PixelHeight != height)
        {
            throw new ProjectFormatException("A layer image doesn't match its declared size.");
        }

        return frame;
    }

    private static byte[] Encode(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
