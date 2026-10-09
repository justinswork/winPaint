namespace WinPaint.Core.Imaging;

/// <summary>
/// A 256×256 block of premultiplied pixels. A tile is either uniform (one value, no storage) or dense.
/// Frozen tiles are shared between surfaces/snapshots and are copied before being written (copy-on-write).
/// </summary>
public sealed class Tile
{
    /// <summary>Tile edge length in pixels.</summary>
    public const int Size = 256;

    /// <summary>log2(<see cref="Size"/>).</summary>
    public const int Shift = 8;

    /// <summary>Pixel count of a tile.</summary>
    public const int Count = Size * Size;

    internal Tile(uint uniform)
    {
        Uniform = uniform;
    }

    internal Tile(uint[] data)
    {
        Data = data;
    }

    /// <summary>Dense data or null when the tile is uniform.</summary>
    internal uint[]? Data { get; set; }

    /// <summary>The uniform value when <see cref="Data"/> is null.</summary>
    internal uint Uniform { get; set; }

    /// <summary>True when shared and must be copied before writing.</summary>
    internal bool Frozen { get; set; }

    /// <summary>True when the tile has no dense storage.</summary>
    public bool IsUniform => Data is null;

    /// <summary>Bytes of dense storage owned by the tile.</summary>
    public long ByteSize => Data is null ? 0 : (long)Count * 4;

    /// <summary>Reads one pixel at tile-local coordinates.</summary>
    public uint Get(int lx, int ly) => Data is null ? Uniform : Data[(ly << Shift) + lx];

    internal Tile CloneUnfrozen() => Data is null ? new Tile(Uniform) : new Tile((uint[])Data.Clone());
}

/// <summary>
/// Sparse tiled pixel surface (premultiplied BGRA32). Missing tiles read as transparent.
/// Snapshots share tiles and are copy-on-write, so taking one costs only an array of references.
/// </summary>
public sealed class TiledSurface
{
    private Tile?[] _tiles;

    /// <summary>Creates an empty (transparent) surface.</summary>
    public TiledSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        TilesX = (width + Tile.Size - 1) >> Tile.Shift;
        TilesY = (height + Tile.Size - 1) >> Tile.Shift;
        _tiles = new Tile?[TilesX * TilesY];
    }

    private TiledSurface(int width, int height, Tile?[] tiles)
        : this(width, height)
    {
        _tiles = tiles;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Tiles per row.</summary>
    public int TilesX { get; }

    /// <summary>Tiles per column.</summary>
    public int TilesY { get; }

    /// <summary>Full bounds.</summary>
    public PixelRect Bounds => new(0, 0, Width, Height);

    /// <summary>True when no tile holds a non-transparent value.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var t in _tiles)
            {
                if (t is not null && (t.Data is not null || t.Uniform != 0))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Enumerates the allocated tiles (for memory accounting).</summary>
    public IEnumerable<Tile> AllocatedTiles => _tiles.Where(t => t is not null)!;

    /// <summary>Returns the tile at tile coordinates (null = transparent).</summary>
    public Tile? GetTile(int tx, int ty) => _tiles[(ty * TilesX) + tx];

    /// <summary>Creates a surface filled with a uniform value (no dense storage).</summary>
    public static TiledSurface CreateFilled(int width, int height, uint value)
    {
        var s = new TiledSurface(width, height);
        if (value != 0)
        {
            for (var i = 0; i < s._tiles.Length; i++)
            {
                s._tiles[i] = new Tile(value);
            }
        }

        return s;
    }

    /// <summary>Reads a pixel (0 when out of range).</summary>
    public uint GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return 0;
        }

        var t = _tiles[((y >> Tile.Shift) * TilesX) + (x >> Tile.Shift)];
        return t?.Get(x & (Tile.Size - 1), y & (Tile.Size - 1)) ?? 0;
    }

    /// <summary>Writes a pixel (ignored when out of range).</summary>
    public void SetPixel(int x, int y, uint value)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        var data = GetWritableData(x >> Tile.Shift, y >> Tile.Shift);
        data[((y & (Tile.Size - 1)) << Tile.Shift) + (x & (Tile.Size - 1))] = value;
    }

    /// <summary>
    /// Returns dense, writable storage for a tile, allocating or un-sharing it as needed.
    /// </summary>
    public uint[] GetWritableData(int tx, int ty)
    {
        var idx = (ty * TilesX) + tx;
        var t = _tiles[idx];
        if (t is null)
        {
            t = new Tile(new uint[Tile.Count]);
            _tiles[idx] = t;
            return t.Data!;
        }

        if (t.Frozen)
        {
            t = t.CloneUnfrozen();
            _tiles[idx] = t;
        }

        if (t.Data is null)
        {
            var d = new uint[Tile.Count];
            if (t.Uniform != 0)
            {
                Array.Fill(d, t.Uniform);
            }

            t.Data = d;
        }

        return t.Data;
    }

    /// <summary>Sets a whole tile to a uniform value (drops dense storage).</summary>
    public void SetTileUniform(int tx, int ty, uint value) =>
        _tiles[(ty * TilesX) + tx] = value == 0 ? null : new Tile(value);

    /// <summary>
    /// Freezes all tiles and returns a new surface sharing them. Further writes to either surface copy the
    /// touched tile first, so both stay independent.
    /// </summary>
    public TiledSurface Snapshot()
    {
        foreach (var t in _tiles)
        {
            if (t is not null)
            {
                t.Frozen = true;
            }
        }

        return new TiledSurface(Width, Height, (Tile?[])_tiles.Clone());
    }

    /// <summary>Copies a rectangle into a dense destination array (row stride in pixels).</summary>
    public void ReadRect(PixelRect r, uint[] dest, int destOffset, int destStride)
    {
        ArgumentNullException.ThrowIfNull(dest);
        var clip = r.Intersect(Bounds);
        for (var y = clip.Y; y < clip.Bottom; y++)
        {
            var ty = y >> Tile.Shift;
            var ly = y & (Tile.Size - 1);
            var x = clip.X;
            var drow = destOffset + ((y - r.Y) * destStride) - r.X;
            while (x < clip.Right)
            {
                var tx = x >> Tile.Shift;
                var tileEnd = Math.Min((tx + 1) << Tile.Shift, clip.Right);
                var t = _tiles[(ty * TilesX) + tx];
                var len = tileEnd - x;
                if (t is null)
                {
                    dest.AsSpan(drow + x, len).Clear();
                }
                else if (t.Data is null)
                {
                    dest.AsSpan(drow + x, len).Fill(t.Uniform);
                }
                else
                {
                    Array.Copy(t.Data, (ly << Tile.Shift) + (x & (Tile.Size - 1)), dest, drow + x, len);
                }

                x = tileEnd;
            }
        }
    }

    /// <summary>Writes a rectangle from a dense source array (row stride in pixels).</summary>
    public void WriteRect(PixelRect r, uint[] src, int srcOffset, int srcStride)
    {
        ArgumentNullException.ThrowIfNull(src);
        var clip = r.Intersect(Bounds);
        for (var y = clip.Y; y < clip.Bottom; y++)
        {
            var ty = y >> Tile.Shift;
            var ly = y & (Tile.Size - 1);
            var x = clip.X;
            var srow = srcOffset + ((y - r.Y) * srcStride) - r.X;
            while (x < clip.Right)
            {
                var tx = x >> Tile.Shift;
                var tileEnd = Math.Min((tx + 1) << Tile.Shift, clip.Right);
                var data = GetWritableData(tx, ty);
                Array.Copy(src, srow + x, data, (ly << Tile.Shift) + (x & (Tile.Size - 1)), tileEnd - x);
                x = tileEnd;
            }
        }
    }

    /// <summary>Fills a rectangle with a value; whole tiles become uniform.</summary>
    public void FillRect(PixelRect r, uint value)
    {
        var clip = r.Intersect(Bounds);
        if (clip.IsEmpty)
        {
            return;
        }

        for (var ty = clip.Y >> Tile.Shift; ty <= (clip.Bottom - 1) >> Tile.Shift; ty++)
        {
            for (var tx = clip.X >> Tile.Shift; tx <= (clip.Right - 1) >> Tile.Shift; tx++)
            {
                var tr = new PixelRect(tx << Tile.Shift, ty << Tile.Shift, Tile.Size, Tile.Size);
                var part = tr.Intersect(clip);
                if (part == tr.Intersect(Bounds))
                {
                    SetTileUniform(tx, ty, value);
                    continue;
                }

                var data = GetWritableData(tx, ty);
                for (var y = part.Y; y < part.Bottom; y++)
                {
                    data.AsSpan(((y - tr.Y) << Tile.Shift) + (part.X - tr.X), part.Width).Fill(value);
                }
            }
        }
    }

    /// <summary>Copies the whole surface into a dense buffer.</summary>
    public PixelBuffer ToPixelBuffer()
    {
        var b = new PixelBuffer(Width, Height);
        ReadRect(Bounds, b.Pixels, 0, Width);
        return b;
    }

    /// <summary>Copies a rectangle into a dense buffer.</summary>
    public PixelBuffer ToPixelBuffer(PixelRect r)
    {
        var b = new PixelBuffer(r.Width, r.Height);
        ReadRect(r, b.Pixels, 0, r.Width);
        return b;
    }

    /// <summary>Creates a surface from a dense buffer, compressing uniform tiles.</summary>
    public static TiledSurface FromPixelBuffer(PixelBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var s = new TiledSurface(buffer.Width, buffer.Height);
        s.WriteRect(buffer.Bounds, buffer.Pixels, 0, buffer.Width);
        s.Compact();
        return s;
    }

    /// <summary>Converts dense tiles whose pixels are all equal into uniform tiles.</summary>
    public void Compact()
    {
        for (var i = 0; i < _tiles.Length; i++)
        {
            var t = _tiles[i];
            if (t?.Data is null)
            {
                continue;
            }

            var tx = i % TilesX;
            var ty = i / TilesX;
            var w = Math.Min(Tile.Size, Width - (tx << Tile.Shift));
            var h = Math.Min(Tile.Size, Height - (ty << Tile.Shift));
            var first = t.Data[0];
            var uniform = true;
            for (var y = 0; y < h && uniform; y++)
            {
                var row = t.Data.AsSpan(y << Tile.Shift, w);
                if (row.ContainsAnyExcept(first))
                {
                    uniform = false;
                }
            }

            if (uniform)
            {
                _tiles[i] = first == 0 ? null : new Tile(first);
            }
        }
    }

    /// <summary>Content equality (pixels inside the bounds).</summary>
    public bool ContentEquals(TiledSurface other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Width == Width && other.Height == Height && ToPixelBuffer().ContentEquals(other.ToPixelBuffer());
    }
}
