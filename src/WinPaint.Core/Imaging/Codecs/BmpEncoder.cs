namespace WinPaint.Core.Imaging.Codecs;

/// <summary>
/// BMP writer: 24-bit (composited on white) for opaque images, 32-bit BITMAPV5 with an alpha mask when the image
/// has transparency.
/// </summary>
public static class BmpEncoder
{
    /// <summary>Writes the image.</summary>
    public static void Write(PixelBuffer image, Stream stream, double dpiX = 96, double dpiY = 96)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        var alpha = image.HasTransparency();
        var bpp = alpha ? 32 : 24;
        var stride = ((image.Width * bpp / 8) + 3) & ~3;
        var headerSize = alpha ? 124 : 40;
        var dataOffset = 14 + headerSize;
        var imageSize = stride * image.Height;

        using var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write((byte)'B');
        w.Write((byte)'M');
        w.Write(dataOffset + imageSize);
        w.Write(0);
        w.Write(dataOffset);

        w.Write(headerSize);
        w.Write(image.Width);
        w.Write(image.Height);
        w.Write((ushort)1);
        w.Write((ushort)bpp);
        w.Write(alpha ? 3 : 0); // BI_BITFIELDS : BI_RGB
        w.Write(imageSize);
        w.Write((int)Math.Round(dpiX / 0.0254));
        w.Write((int)Math.Round(dpiY / 0.0254));
        w.Write(0);
        w.Write(0);
        if (alpha)
        {
            w.Write(0x00FF0000u);
            w.Write(0x0000FF00u);
            w.Write(0x000000FFu);
            w.Write(0xFF000000u);
            w.Write(0x73524742); // LCS_sRGB
            w.Write(new byte[36]);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(4); // LCS_GM_IMAGES
            w.Write(0);
            w.Write(0);
            w.Write(0);
        }

        var row = new byte[stride];
        for (var y = image.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var p = image[x, y];
                if (alpha)
                {
                    var s = ColorUtil.Unpremultiply(p);
                    var o = x * 4;
                    row[o] = ColorUtil.B(s);
                    row[o + 1] = ColorUtil.G(s);
                    row[o + 2] = ColorUtil.R(s);
                    row[o + 3] = ColorUtil.A(s);
                }
                else
                {
                    var o = x * 3;
                    row[o] = ColorUtil.B(p);
                    row[o + 1] = ColorUtil.G(p);
                    row[o + 2] = ColorUtil.R(p);
                }
            }

            w.Write(row);
        }
    }
}
