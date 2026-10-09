namespace WinPaint.Core.Imaging.Codecs;

/// <summary>
/// Single-frame GIF89a writer with octree color quantization (≤256 colors) and binary transparency.
/// </summary>
public static class GifEncoder
{
    /// <summary>Writes the image as a GIF.</summary>
    public static void Write(PixelBuffer image, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        var (palette, indices, transparentIndex) = Quantize(image);

        var tableBits = 1;
        while ((1 << tableBits) < Math.Max(2, palette.Count))
        {
            tableBits++;
        }

        using var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("GIF89a"u8.ToArray());
        w.Write((ushort)image.Width);
        w.Write((ushort)image.Height);
        w.Write((byte)(0x80 | ((tableBits - 1) << 4) | (tableBits - 1)));
        w.Write((byte)0);
        w.Write((byte)0);
        for (var i = 0; i < 1 << tableBits; i++)
        {
            var c = i < palette.Count ? palette[i] : 0u;
            w.Write(ColorUtil.R(c));
            w.Write(ColorUtil.G(c));
            w.Write(ColorUtil.B(c));
        }

        if (transparentIndex >= 0)
        {
            w.Write((byte)0x21);
            w.Write((byte)0xF9);
            w.Write((byte)4);
            w.Write((byte)0x01);
            w.Write((ushort)0);
            w.Write((byte)transparentIndex);
            w.Write((byte)0);
        }

        w.Write((byte)0x2C);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)image.Width);
        w.Write((ushort)image.Height);
        w.Write((byte)0);
        var minCodeSize = Math.Max(2, tableBits);
        w.Write((byte)minCodeSize);
        w.Flush();
        LzwEncode(indices, minCodeSize, stream);
        stream.WriteByte(0x3B);
    }

    /// <summary>
    /// Quantizes to at most 256 colors (255 + one transparent slot when needed). Pixels with alpha &lt; 128 are transparent.
    /// Returns opaque straight RGB palette entries.
    /// </summary>
    public static (List<uint> Palette, byte[] Indices, int TransparentIndex) Quantize(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var hasTransparent = false;
        var octree = new Octree();
        var exact = new Dictionary<uint, int>();
        foreach (var p in image.Pixels)
        {
            if (ColorUtil.A(p) < 128)
            {
                hasTransparent = true;
                continue;
            }

            var s = ColorUtil.Unpremultiply(p) | 0xFF000000;
            if (exact.Count <= 256)
            {
                exact.TryAdd(s, exact.Count);
            }
        }

        var maxColors = hasTransparent ? 255 : 256;
        List<uint> palette;
        Func<uint, int> map;
        if (exact.Count <= maxColors)
        {
            palette = [.. exact.Keys];
            map = c => exact[c];
        }
        else
        {
            foreach (var p in image.Pixels)
            {
                if (ColorUtil.A(p) >= 128)
                {
                    octree.Add(ColorUtil.Unpremultiply(p));
                }
            }

            octree.Reduce(maxColors);
            palette = octree.BuildPalette();
            map = octree.IndexOf;
        }

        var transparentIndex = hasTransparent ? palette.Count : -1;
        if (hasTransparent)
        {
            palette.Add(0xFF000000);
        }

        var indices = new byte[image.Pixels.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            var p = image.Pixels[i];
            indices[i] = ColorUtil.A(p) < 128 ? (byte)transparentIndex : (byte)map(ColorUtil.Unpremultiply(p) | 0xFF000000);
        }

        return (palette, indices, transparentIndex);
    }

    /// <summary>GIF variable-length LZW (mirrors the classic Unix compress code-size schedule).</summary>
    private static void LzwEncode(byte[] data, int minCodeSize, Stream output)
    {
        var clearCode = 1 << minCodeSize;
        var eoi = clearCode + 1;
        var initBits = minCodeSize + 1;
        var nBits = initBits;
        var maxCode = (1 << nBits) - 1;
        var freeEnt = clearCode + 2;
        var clearFlag = false;
        var dict = new Dictionary<int, int>(4096);
        var block = new byte[256];
        var blockLen = 0;
        var acc = 0;
        var accBits = 0;

        void FlushByte(byte b)
        {
            block[blockLen++] = b;
            if (blockLen == 255)
            {
                output.WriteByte(255);
                output.Write(block, 0, 255);
                blockLen = 0;
            }
        }

        void Emit(int code)
        {
            acc |= code << accBits;
            accBits += nBits;
            while (accBits >= 8)
            {
                FlushByte((byte)acc);
                acc >>= 8;
                accBits -= 8;
            }

            if (clearFlag)
            {
                nBits = initBits;
                maxCode = (1 << nBits) - 1;
                clearFlag = false;
            }
            else if (freeEnt > maxCode)
            {
                nBits++;
                maxCode = nBits == 12 ? 4096 : (1 << nBits) - 1;
            }
        }

        Emit(clearCode);
        if (data.Length > 0)
        {
            int ent = data[0];
            for (var i = 1; i < data.Length; i++)
            {
                int c = data[i];
                var key = (ent << 8) | c;
                if (dict.TryGetValue(key, out var code))
                {
                    ent = code;
                    continue;
                }

                Emit(ent);
                ent = c;
                if (freeEnt < 4096)
                {
                    dict[key] = freeEnt++;
                }
                else
                {
                    dict.Clear();
                    freeEnt = clearCode + 2;
                    clearFlag = true;
                    Emit(clearCode);
                }
            }

            Emit(ent);
        }

        Emit(eoi);
        if (accBits > 0)
        {
            FlushByte((byte)acc);
        }

        if (blockLen > 0)
        {
            output.WriteByte((byte)blockLen);
            output.Write(block, 0, blockLen);
        }

        output.WriteByte(0);
    }

    private sealed class Octree
    {
        private readonly Node _root = new(0);
        private readonly List<Node>[] _levels = Enumerable.Range(0, 8).Select(_ => new List<Node>()).ToArray();
        private int _leafCount;

        public void Add(uint straight)
        {
            var node = _root;
            for (var level = 0; level < 8; level++)
            {
                if (node.IsLeaf)
                {
                    break;
                }

                var idx = ChildIndex(straight, level);
                if (node.Children[idx] is null)
                {
                    var child = new Node(level + 1);
                    node.Children[idx] = child;
                    if (level + 1 == 8)
                    {
                        child.IsLeaf = true;
                        _leafCount++;
                    }
                    else
                    {
                        _levels[level + 1].Add(child);
                    }
                }

                node = node.Children[idx]!;
            }

            node.Count++;
            node.R += ColorUtil.R(straight);
            node.G += ColorUtil.G(straight);
            node.B += ColorUtil.B(straight);
        }

        public void Reduce(int maxColors)
        {
            for (var level = 7; level > 0 && _leafCount > maxColors; level--)
            {
                var nodes = _levels[level].Where(n => !n.IsLeaf).OrderBy(n => n.SubtreeCount()).ToList();
                foreach (var n in nodes)
                {
                    if (_leafCount <= maxColors)
                    {
                        break;
                    }

                    var merged = 0;
                    for (var i = 0; i < 8; i++)
                    {
                        var c = n.Children[i];
                        if (c is null)
                        {
                            continue;
                        }

                        n.Absorb(c);
                        n.Children[i] = null;
                        merged++;
                    }

                    n.IsLeaf = true;
                    _leafCount -= merged - 1;
                }
            }
        }

        public List<uint> BuildPalette()
        {
            var palette = new List<uint>();
            Walk(_root, palette);
            return palette;
        }

        public int IndexOf(uint straight)
        {
            var node = _root;
            for (var level = 0; level < 8 && !node.IsLeaf; level++)
            {
                var next = node.Children[ChildIndex(straight, level)];
                if (next is null)
                {
                    next = node.Children.First(c => c is not null)!;
                }

                node = next;
            }

            return node.PaletteIndex;
        }

        private static int ChildIndex(uint c, int level)
        {
            var shift = 7 - level;
            return (((ColorUtil.R(c) >> shift) & 1) << 2) | (((ColorUtil.G(c) >> shift) & 1) << 1) | ((ColorUtil.B(c) >> shift) & 1);
        }

        private static void Walk(Node n, List<uint> palette)
        {
            if (n.IsLeaf)
            {
                n.PaletteIndex = palette.Count;
                var cnt = Math.Max(1, n.Count);
                palette.Add(ColorUtil.Pack(255, (byte)(n.R / cnt), (byte)(n.G / cnt), (byte)(n.B / cnt)));
                return;
            }

            foreach (var c in n.Children)
            {
                if (c is not null)
                {
                    Walk(c, palette);
                }
            }
        }

        private sealed class Node(int level)
        {
            public Node?[] Children { get; } = new Node?[8];

            public bool IsLeaf { get; set; }

            public int Level { get; } = level;

            public long Count { get; set; }

            public long R { get; set; }

            public long G { get; set; }

            public long B { get; set; }

            public int PaletteIndex { get; set; }

            public long SubtreeCount() => Count + Children.Sum(c => c?.SubtreeCount() ?? 0);

            public void Absorb(Node c)
            {
                if (!c.IsLeaf)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        if (c.Children[i] is { } gc)
                        {
                            c.Absorb(gc);
                        }
                    }
                }

                Count += c.Count;
                R += c.R;
                G += c.G;
                B += c.B;
            }
        }
    }
}
