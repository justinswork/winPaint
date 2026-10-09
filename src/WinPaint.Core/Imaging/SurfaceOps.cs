namespace WinPaint.Core.Imaging;

/// <summary>Compositing primitives between tiled surfaces and dense accumulation buffers.</summary>
public static class SurfaceOps
{
    /// <summary>Composites the surface's pixels in <paramref name="r"/> over a dense buffer (source-over).</summary>
    public static void OverInto(TiledSurface s, PixelRect r, uint[] dest, int destOffset, int destStride)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(dest);
        ForEachTilePart(s, r, (tile, part, tileRect) =>
        {
            for (var y = part.Y; y < part.Bottom; y++)
            {
                var drow = destOffset + ((y - r.Y) * destStride) - r.X;
                var span = dest.AsSpan(drow + part.X, part.Width);
                if (tile.Data is null)
                {
                    var u = tile.Uniform;
                    if (u >= 0xFF000000)
                    {
                        span.Fill(u);
                    }
                    else
                    {
                        for (var i = 0; i < span.Length; i++)
                        {
                            span[i] = ColorUtil.Over(u, span[i]);
                        }
                    }
                }
                else
                {
                    var srow = tile.Data.AsSpan(((y - tileRect.Y) << Tile.Shift) + (part.X - tileRect.X), part.Width);
                    Blend.Row(BlendMode.Normal, srow, span, 255);
                }
            }
        });
    }

    /// <summary>Multiplies the dense buffer by (1 − erase) using the erase mask's low byte.</summary>
    public static void EraseInto(TiledSurface erase, PixelRect r, uint[] dest, int destOffset, int destStride)
    {
        ArgumentNullException.ThrowIfNull(erase);
        ArgumentNullException.ThrowIfNull(dest);
        ForEachTilePart(erase, r, (tile, part, tileRect) =>
        {
            for (var y = part.Y; y < part.Bottom; y++)
            {
                var drow = destOffset + ((y - r.Y) * destStride) - r.X;
                var span = dest.AsSpan(drow + part.X, part.Width);
                if (tile.Data is null)
                {
                    var keep = 255 - (int)(tile.Uniform & 0xFF);
                    for (var i = 0; i < span.Length; i++)
                    {
                        span[i] = ColorUtil.Scale(span[i], keep);
                    }
                }
                else
                {
                    var srow = tile.Data.AsSpan(((y - tileRect.Y) << Tile.Shift) + (part.X - tileRect.X), part.Width);
                    for (var i = 0; i < span.Length; i++)
                    {
                        var e = (int)(srow[i] & 0xFF);
                        if (e != 0)
                        {
                            span[i] = ColorUtil.Scale(span[i], 255 - e);
                        }
                    }
                }
            }
        });
    }

    /// <summary>Composites a positioned dense buffer over the dense destination region <paramref name="r"/>.</summary>
    public static void OverInto(PixelBuffer src, int srcX, int srcY, PixelRect r, uint[] dest, int destOffset, int destStride)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(dest);
        var part = new PixelRect(srcX, srcY, src.Width, src.Height).Intersect(r);
        for (var y = part.Y; y < part.Bottom; y++)
        {
            var span = dest.AsSpan(destOffset + ((y - r.Y) * destStride) + (part.X - r.X), part.Width);
            var srow = src.Pixels.AsSpan(((y - srcY) * src.Width) + (part.X - srcX), part.Width);
            Blend.Row(BlendMode.Normal, srow, span, 255);
        }
    }

    /// <summary>Invokes <paramref name="action"/> for each non-null tile intersecting <paramref name="r"/>.</summary>
    public static void ForEachTilePart(TiledSurface s, PixelRect r, Action<Tile, PixelRect, PixelRect> action)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(action);
        var clip = r.Intersect(s.Bounds);
        if (clip.IsEmpty)
        {
            return;
        }

        for (var ty = clip.Y >> Tile.Shift; ty <= (clip.Bottom - 1) >> Tile.Shift; ty++)
        {
            for (var tx = clip.X >> Tile.Shift; tx <= (clip.Right - 1) >> Tile.Shift; tx++)
            {
                var tile = s.GetTile(tx, ty);
                if (tile is null)
                {
                    continue;
                }

                var tileRect = new PixelRect(tx << Tile.Shift, ty << Tile.Shift, Tile.Size, Tile.Size);
                action(tile, tileRect.Intersect(clip), tileRect);
            }
        }
    }
}
