namespace WinPaint.Core.Imaging.Codecs;

/// <summary>
/// Windows icon writer: 256-px entries are PNG-compressed, smaller entries are 32-bit BMP (DIB + AND mask).
/// Non-square images are padded (centered, transparent) to a square first.
/// </summary>
public static class IcoEncoder
{
    /// <summary>Sizes offered by the save dialog.</summary>
    public static IReadOnlyList<int> AvailableSizes { get; } = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>Pads an image to a square, centering it on a transparent background.</summary>
    public static PixelBuffer PadToSquare(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width == image.Height)
        {
            return image;
        }

        var size = Math.Max(image.Width, image.Height);
        var sq = new PixelBuffer(size, size);
        sq.Blit(image, (size - image.Width) / 2, (size - image.Height) / 2);
        return sq;
    }

    /// <summary>Writes an icon with the requested sizes.</summary>
    public static void Write(PixelBuffer image, Stream stream, IReadOnlyList<int> sizes)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sizes);
        var square = PadToSquare(image);
        var list = sizes.Where(s => s is >= 1 and <= 256).Distinct().OrderBy(s => s).ToList();
        if (list.Count == 0)
        {
            list.Add(Math.Min(256, square.Width));
        }

        var payloads = new List<byte[]>();
        foreach (var s in list)
        {
            var img = Resampler.Resize(square, s, s, s >= square.Width ? ResampleMode.NearestNeighbor : ResampleMode.HighQuality);
            payloads.Add(s >= 256 ? ImageCodec.EncodePng(img) : EncodeDib(img));
        }

        using var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)list.Count);
        var offset = 6 + (16 * list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(payloads[i].Length);
            w.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var p in payloads)
        {
            w.Write(p);
        }
    }

    private static byte[] EncodeDib(PixelBuffer img)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var size = img.Width;
        w.Write(40);
        w.Write(size);
        w.Write(size * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);

        // XOR (color) bitmap, bottom-up, straight BGRA.
        for (var y = size - 1; y >= 0; y--)
        {
            for (var x = 0; x < size; x++)
            {
                w.Write(ColorUtil.Unpremultiply(img[x, y]));
            }
        }

        // AND mask, 1 bpp rows padded to 32 bits; 1 = transparent.
        var maskStride = ((size + 31) / 32) * 4;
        for (var y = size - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (var x = 0; x < size; x++)
            {
                if (ColorUtil.A(img[x, y]) == 0)
                {
                    row[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }

            w.Write(row);
        }

        w.Flush();
        return ms.ToArray();
    }
}
