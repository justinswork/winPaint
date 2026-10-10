using System.Buffers.Binary;
using System.Text;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.Core.Projects;

/// <summary>
/// Embeds a project container in PNG, TIFF, JPEG and GIF files and finds it again (docs/specs/wpp-format.md §6).
/// The payload is an 8-byte header (<c>WPP1</c>, encoding version 1, reserved 0) followed by the container.
/// </summary>
public static class ProjectEmbedding
{
    /// <summary>Payload encoding version.</summary>
    public const ushort EncodingVersion = 1;

    /// <summary>TIFF tag holding the payload.</summary>
    public const ushort TiffTag = 65129;

    private const int HeaderSize = 8;
    private const int JpegSegmentPayload = 65_520;
    private static readonly byte[] Magic = "WPP1"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngChunkType = "wpRJ"u8.ToArray();
    private static readonly byte[] JpegIdentifier = "WINPAINT\0"u8.ToArray();
    private static readonly byte[] GifApplication = "WINPAINT1.0"u8.ToArray();

    /// <summary>True for formats that can carry a project.</summary>
    public static bool CanEmbed(ImageFormat format) => format is ImageFormat.Png or ImageFormat.Tiff or ImageFormat.Jpeg or ImageFormat.Gif;

    /// <summary>Returns <paramref name="image"/> (encoded as <paramref name="format"/>) with the container embedded.</summary>
    public static byte[] Embed(byte[] image, ImageFormat format, byte[] container)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(container);
        if ((long)container.Length + HeaderSize > ProjectFormat.MaxEmbeddedPayload)
        {
            throw new ProjectFormatException("The project is too large to embed in an image.");
        }

        var payload = new byte[container.Length + HeaderSize];
        Magic.CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), EncodingVersion);
        container.CopyTo(payload, HeaderSize);
        return format switch
        {
            ImageFormat.Png => EmbedPng(image, payload),
            ImageFormat.Jpeg => EmbedJpeg(image, payload),
            ImageFormat.Gif => EmbedGif(image, payload),
            ImageFormat.Tiff => EmbedTiff(image, payload),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "This format can't carry a project."),
        };
    }

    /// <summary>
    /// Finds an embedded container (format detected from the file signature). Returns null when there is none;
    /// throws <see cref="ProjectFormatException"/> when a payload is present but damaged or unsupported.
    /// </summary>
    public static byte[]? Extract(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        byte[]? payload;
        try
        {
            payload = DetectFormat(image) switch
            {
                ImageFormat.Png => ExtractPng(image),
                ImageFormat.Jpeg => ExtractJpeg(image),
                ImageFormat.Gif => ExtractGif(image),
                ImageFormat.Tiff => ExtractTiff(image),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            throw new ProjectFormatException("The image's embedded project is damaged.", ex);
        }

        if (payload is null)
        {
            return null;
        }

        if (payload.Length < HeaderSize || !payload.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new ProjectFormatException("The image's embedded project is damaged.");
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4)) != EncodingVersion)
        {
            throw new ProjectFormatException("The image's embedded project was saved by a newer version of winPaint.") { IsNewerMajorVersion = true };
        }

        return payload[HeaderSize..];
    }

    /// <summary>Image format from the file signature, or null.</summary>
    public static ImageFormat? DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature))
        {
            return ImageFormat.Png;
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return ImageFormat.Gif;
        }

        if (data.StartsWith("II*\0"u8) || data.StartsWith("MM\0*"u8))
        {
            return ImageFormat.Tiff;
        }

        return null;
    }

    // ---- PNG: private ancillary unsafe-to-copy chunks "wpRJ" before IEND ----
    private static byte[] EmbedPng(byte[] png, byte[] payload)
    {
        var chunks = WalkPng(png);
        var iend = chunks.FindLast(c => c.Type == "IEND");
        if (iend.Type is null)
        {
            throw new ProjectFormatException("The PNG has no end chunk.");
        }

        using var ms = new MemoryStream(png.Length + payload.Length + 64);
        ms.Write(png, 0, iend.Start);
        Span<byte> head = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(head, payload.Length);
        ms.Write(head);
        ms.Write(PngChunkType);
        ms.Write(payload);
        BinaryPrimitives.WriteUInt32BigEndian(head, Crc32(payload, Crc32Update(0xFFFFFFFFu, PngChunkType)) ^ 0xFFFFFFFFu);
        ms.Write(head);
        ms.Write(png, iend.Start, png.Length - iend.Start);
        return ms.ToArray();
    }

    private static byte[]? ExtractPng(byte[] png)
    {
        using var ms = new MemoryStream();
        var found = false;
        foreach (var c in WalkPng(png))
        {
            if (c.Type != "wpRJ")
            {
                continue;
            }

            var typeAndData = png.AsSpan(c.Start + 4, 4 + c.Length);
            if ((Crc32Update(0xFFFFFFFFu, typeAndData) ^ 0xFFFFFFFFu) != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(c.Start + 8 + c.Length)))
            {
                throw new ProjectFormatException("The image's embedded project is damaged.");
            }

            ms.Write(typeAndData[4..]);
            found = true;
        }

        return found ? ms.ToArray() : null;
    }

    private static List<(string Type, int Start, int Length)> WalkPng(byte[] png)
    {
        var list = new List<(string, int, int)>();
        var pos = PngSignature.Length;
        while (pos + 12 <= png.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            if (len < 0 || (long)pos + 12 + len > png.Length)
            {
                break;
            }

            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            list.Add((type, pos, len));
            pos += 12 + len;
            if (type == "IEND")
            {
                break;
            }
        }

        return list;
    }

    // ---- JPEG: APP11 segments "WINPAINT\0" + sequence + count + data, after SOI/APP0/APP1 ----
    private static byte[] EmbedJpeg(byte[] jpg, byte[] payload)
    {
        var insert = 2;
        while (insert + 4 <= jpg.Length && jpg[insert] == 0xFF && jpg[insert + 1] is 0xE0 or 0xE1)
        {
            insert += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpg.AsSpan(insert + 2));
        }

        var count = (payload.Length + JpegSegmentPayload - 1) / JpegSegmentPayload;
        if (count > ushort.MaxValue)
        {
            throw new ProjectFormatException("The project is too large to embed in a JPEG.");
        }

        using var ms = new MemoryStream(jpg.Length + payload.Length + (count * 20));
        ms.Write(jpg, 0, insert);
        Span<byte> head = stackalloc byte[8];
        for (var i = 0; i < count; i++)
        {
            var chunk = payload.AsSpan(i * JpegSegmentPayload, Math.Min(JpegSegmentPayload, payload.Length - (i * JpegSegmentPayload)));
            head[0] = 0xFF;
            head[1] = 0xEB;
            BinaryPrimitives.WriteUInt16BigEndian(head[2..], (ushort)(2 + JpegIdentifier.Length + 4 + chunk.Length));
            ms.Write(head[..4]);
            ms.Write(JpegIdentifier);
            BinaryPrimitives.WriteUInt16BigEndian(head, (ushort)(i + 1));
            BinaryPrimitives.WriteUInt16BigEndian(head[2..], (ushort)count);
            ms.Write(head[..4]);
            ms.Write(chunk);
        }

        ms.Write(jpg, insert, jpg.Length - insert);
        return ms.ToArray();
    }

    private static byte[]? ExtractJpeg(byte[] jpg)
    {
        var parts = new SortedDictionary<int, (int Start, int Length)>();
        var total = -1;
        var pos = 2;
        while (pos + 4 <= jpg.Length)
        {
            if (jpg[pos] != 0xFF)
            {
                break;
            }

            var marker = jpg[pos + 1];
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                break;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                pos += 2;
                continue;
            }

            var len = BinaryPrimitives.ReadUInt16BigEndian(jpg.AsSpan(pos + 2));
            if (len < 2 || pos + 2 + len > jpg.Length)
            {
                break;
            }

            var body = jpg.AsSpan(pos + 4, len - 2);
            if (marker == 0xEB && body.StartsWith(JpegIdentifier))
            {
                if (body.Length < JpegIdentifier.Length + 4)
                {
                    throw new ProjectFormatException("The image's embedded project is damaged.");
                }

                int seq = BinaryPrimitives.ReadUInt16BigEndian(body[JpegIdentifier.Length..]);
                int count = BinaryPrimitives.ReadUInt16BigEndian(body[(JpegIdentifier.Length + 2)..]);
                if (seq < 1 || seq > count || (total >= 0 && total != count) || !parts.TryAdd(seq, (pos + 4 + JpegIdentifier.Length + 4, body.Length - JpegIdentifier.Length - 4)))
                {
                    throw new ProjectFormatException("The image's embedded project is damaged.");
                }

                total = count;
            }

            pos += 2 + len;
        }

        if (total < 0)
        {
            return null;
        }

        if (parts.Count != total)
        {
            throw new ProjectFormatException("The image's embedded project is incomplete.");
        }

        using var ms = new MemoryStream();
        foreach (var (start, length) in parts.Values)
        {
            ms.Write(jpg, start, length);
        }

        return ms.ToArray();
    }

    // ---- GIF: Application Extension "WINPAINT" / "1.0" after the global color table ----
    private static byte[] EmbedGif(byte[] gif, byte[] payload)
    {
        var insert = GifBlocksStart(gif);
        using var ms = new MemoryStream(gif.Length + payload.Length + (payload.Length / 255) + 32);
        ms.Write(gif, 0, insert);
        ms.Write([0x21, 0xFF, (byte)GifApplication.Length]);
        ms.Write(GifApplication);
        for (var i = 0; i < payload.Length; i += 255)
        {
            var n = Math.Min(255, payload.Length - i);
            ms.WriteByte((byte)n);
            ms.Write(payload, i, n);
        }

        ms.WriteByte(0);
        ms.Write(gif, insert, gif.Length - insert);
        var result = ms.ToArray();
        "GIF89a"u8.CopyTo(result);
        return result;
    }

    private static byte[]? ExtractGif(byte[] gif)
    {
        var pos = GifBlocksStart(gif);
        while (pos < gif.Length)
        {
            switch (gif[pos])
            {
                case 0x3B:
                    return null;
                case 0x21:
                    var label = gif[pos + 1];
                    pos += 2;
                    if (label == 0xFF && gif[pos] == GifApplication.Length && gif.AsSpan(pos + 1, GifApplication.Length).SequenceEqual(GifApplication))
                    {
                        pos += 1 + GifApplication.Length;
                        using var ms = new MemoryStream();
                        while (gif[pos] != 0)
                        {
                            ms.Write(gif, pos + 1, gif[pos]);
                            pos += 1 + gif[pos];
                        }

                        return ms.ToArray();
                    }

                    pos = SkipSubBlocks(gif, pos);
                    break;
                case 0x2C:
                    var packed = gif[pos + 9];
                    pos += 10;
                    if ((packed & 0x80) != 0)
                    {
                        pos += 3 << ((packed & 7) + 1);
                    }

                    pos = SkipSubBlocks(gif, pos + 1);
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    private static int GifBlocksStart(byte[] gif)
    {
        var packed = gif[10];
        return 13 + ((packed & 0x80) != 0 ? 3 << ((packed & 7) + 1) : 0);
    }

    private static int SkipSubBlocks(byte[] gif, int pos)
    {
        while (gif[pos] != 0)
        {
            pos += 1 + gif[pos];
        }

        return pos + 1;
    }

    // ---- TIFF: private tag 65129 (UNDEFINED) in IFD0. IFD0 is rewritten at the end with the new entry. ----
    private static byte[] EmbedTiff(byte[] tif, byte[] payload)
    {
        var le = tif[0] == (byte)'I';
        var ifd = (int)ReadU32(tif, 4, le);
        int count = ReadU16(tif, ifd, le);
        var entries = new List<byte[]>();
        for (var i = 0; i < count; i++)
        {
            var e = tif.AsSpan(ifd + 2 + (i * 12), 12).ToArray();
            if (ReadU16(e, 0, le) != TiffTag)
            {
                entries.Add(e);
            }
        }

        var next = ReadU32(tif, ifd + 2 + (count * 12), le);
        var payloadOffset = Align(tif.Length);
        var ifdOffset = Align(payloadOffset + payload.Length);
        if ((long)ifdOffset + 2 + ((entries.Count + 1) * 12) + 4 > uint.MaxValue)
        {
            throw new ProjectFormatException("The project is too large to embed in a TIFF.");
        }

        var tag = new byte[12];
        WriteU16(tag, 0, TiffTag, le);
        WriteU16(tag, 2, 7, le);
        WriteU32(tag, 4, (uint)payload.Length, le);
        WriteU32(tag, 8, (uint)payloadOffset, le);
        entries.Add(tag);
        entries.Sort((a, b) => ReadU16(a, 0, le).CompareTo(ReadU16(b, 0, le)));

        var result = new byte[ifdOffset + 2 + (entries.Count * 12) + 4];
        tif.CopyTo(result, 0);
        payload.CopyTo(result, payloadOffset);
        WriteU16(result, ifdOffset, (ushort)entries.Count, le);
        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].CopyTo(result, ifdOffset + 2 + (i * 12));
        }

        WriteU32(result, ifdOffset + 2 + (entries.Count * 12), next, le);
        WriteU32(result, 4, (uint)ifdOffset, le);
        return result;
    }

    private static byte[]? ExtractTiff(byte[] tif)
    {
        var le = tif[0] == (byte)'I';
        var ifd = ReadU32(tif, 4, le);
        int count = ReadU16(tif, checked((int)ifd), le);
        for (var i = 0; i < count; i++)
        {
            var e = checked((int)ifd + 2 + (i * 12));
            if (ReadU16(tif, e, le) != TiffTag)
            {
                continue;
            }

            var type = ReadU16(tif, e + 2, le);
            var length = ReadU32(tif, e + 4, le);
            if (type is not (1 or 7) || length > int.MaxValue)
            {
                throw new ProjectFormatException("The image's embedded project is damaged.");
            }

            var offset = length <= 4 ? (uint)(e + 8) : ReadU32(tif, e + 8, le);
            if ((long)offset + length > tif.Length)
            {
                throw new ProjectFormatException("The image's embedded project is damaged.");
            }

            return tif.AsSpan((int)offset, (int)length).ToArray();
        }

        return null;
    }

    private static int Align(int v) => (v + 1) & ~1;

    private static ushort ReadU16(byte[] b, int o, bool le) =>
        le ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));

    private static uint ReadU32(byte[] b, int o, bool le) =>
        le ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));

    private static void WriteU16(byte[] b, int o, ushort v, bool le)
    {
        if (le)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o), v);
        }
    }

    private static void WriteU32(byte[] b, int o, uint v, bool le)
    {
        if (le)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o), v);
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> data, uint crc) => Crc32Update(crc, data);

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }
}
