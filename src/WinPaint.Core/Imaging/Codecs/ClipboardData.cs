using System.Windows;
using System.Windows.Media.Imaging;

namespace WinPaint.Core.Imaging.Codecs;

/// <summary>
/// Builds and reads clipboard data objects for images: PNG (keeps alpha), DIB and the standard WPF bitmap.
/// Reading prefers PNG, then DIB/DIBV5, then the bitmap format.
/// </summary>
public static class ClipboardData
{
    /// <summary>The registered PNG clipboard format name.</summary>
    public const string PngFormat = "PNG";

    /// <summary>Creates a data object holding the image in all supported formats.</summary>
    public static DataObject Create(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var data = new DataObject();
        data.SetData(PngFormat, new MemoryStream(ImageCodec.EncodePng(image)), false);
        data.SetData(DataFormats.Dib, new MemoryStream(EncodeDib(image)), false);
        data.SetImage(ImageCodec.ToBgr24OnWhite(image));
        return data;
    }

    /// <summary>True when the data object contains an image in a readable format.</summary>
    public static bool ContainsImage(IDataObject? data) =>
        data is not null && (data.GetDataPresent(PngFormat) || data.GetDataPresent(DataFormats.Dib) || data.GetDataPresent(DataFormats.Bitmap));

    /// <summary>Reads an image (PNG, then DIB, then bitmap). Returns null when there is none.</summary>
    public static PixelBuffer? Read(IDataObject? data)
    {
        if (data is null)
        {
            return null;
        }

        if (data.GetDataPresent(PngFormat) && data.GetData(PngFormat) is MemoryStream png)
        {
            try
            {
                png.Position = 0;
                return ImageCodec.Decode(png).Pixels;
            }
            catch (ImageOpenException)
            {
                // Fall through to the other formats.
            }
        }

        if (data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib)
        {
            var img = DecodeDib(dib.ToArray());
            if (img is not null)
            {
                return img;
            }
        }

        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
        {
            return PixelBuffer.FromBitmapSource(bmp);
        }

        return null;
    }

    /// <summary>Encodes a bottom-up 32-bit BI_RGB DIB (straight alpha in the 4th byte).</summary>
    public static byte[] EncodeDib(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40);
        w.Write(image.Width);
        w.Write(image.Height);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(image.Width * image.Height * 4);
        w.Write(3780);
        w.Write(3780);
        w.Write(0);
        w.Write(0);
        for (var y = image.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < image.Width; x++)
            {
                w.Write(ColorUtil.Unpremultiply(image[x, y]));
            }
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Decodes a packed DIB (BITMAPINFOHEADER/V4/V5) by prefixing a file header and using the BMP decoder.</summary>
    public static PixelBuffer? DecodeDib(byte[] dib)
    {
        ArgumentNullException.ThrowIfNull(dib);
        if (dib.Length < 40)
        {
            return null;
        }

        var headerSize = BitConverter.ToInt32(dib, 0);
        var bitCount = BitConverter.ToUInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var clrUsed = BitConverter.ToInt32(dib, 32);
        var masks = compression == 3 && headerSize == 40 ? 12 : 0;
        var palette = bitCount <= 8 ? (clrUsed == 0 ? 1 << bitCount : clrUsed) * 4 : 0;
        var offset = 14 + headerSize + masks + palette;
        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(offset).CopyTo(file, 10);
        dib.CopyTo(file, 14);
        try
        {
            var img = ImageCodec.Decode(new MemoryStream(file)).Pixels;

            // A 32-bit BI_RGB DIB whose alpha bytes are all zero is really opaque.
            if (bitCount == 32 && compression == 0 && img.Pixels.All(p => ColorUtil.A(p) == 0))
            {
                var stride = BitConverter.ToInt32(dib, 4) * 4;
                var h = Math.Abs(BitConverter.ToInt32(dib, 8));
                var topDown = BitConverter.ToInt32(dib, 8) < 0;
                for (var y = 0; y < img.Height; y++)
                {
                    var srcRow = topDown ? y : h - 1 - y;
                    for (var x = 0; x < img.Width; x++)
                    {
                        var o = headerSize + (srcRow * stride) + (x * 4);
                        img[x, y] = ColorUtil.Pack(255, dib[o + 2], dib[o + 1], dib[o]);
                    }
                }
            }

            return img;
        }
        catch (ImageOpenException)
        {
            return null;
        }
    }
}
