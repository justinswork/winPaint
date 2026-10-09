using System.Windows;
using System.Windows.Media;

namespace WinPaint.Core.Brushes;

/// <summary>Low-level rasterization primitives writing into a <see cref="StrokeCanvas"/>.</summary>
public static class Raster
{
    /// <summary>Antialiased disk. <paramref name="softness"/> widens the edge falloff (px).</summary>
    public static void Disk(StrokeCanvas c, Point center, double radius, Color color, StrokeCombine combine, double softness = 1.0, Func<int, int, double>? texture = null)
    {
        ArgumentNullException.ThrowIfNull(c);
        radius = Math.Max(0.5, radius);
        var x0 = (int)Math.Floor(center.X - radius - softness);
        var x1 = (int)Math.Ceiling(center.X + radius + softness);
        var y0 = (int)Math.Floor(center.Y - radius - softness);
        var y1 = (int)Math.Ceiling(center.Y + radius + softness);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5 - center.X;
                var dy = y + 0.5 - center.Y;
                var d = Math.Sqrt((dx * dx) + (dy * dy));
                var cov = Math.Clamp((radius + (softness * 0.5) - d) / softness, 0, 1);
                if (cov <= 0)
                {
                    continue;
                }

                if (texture is not null)
                {
                    cov *= texture(x, y);
                }

                c.PlotCoverage(x, y, color, cov, combine);
            }
        }
    }

    /// <summary>Hard-edged (aliased) disk; radius &lt; 1 plots a single pixel.</summary>
    public static void HardDisk(StrokeCanvas c, Point center, double diameter, uint premultiplied)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (diameter <= 1.5)
        {
            c.Plot((int)Math.Floor(center.X), (int)Math.Floor(center.Y), premultiplied, StrokeCombine.Max);
            return;
        }

        var r = diameter / 2.0;
        var cx = Math.Floor(center.X) + (diameter % 2 == 1 ? 0.5 : 0);
        var cy = Math.Floor(center.Y) + (diameter % 2 == 1 ? 0.5 : 0);
        for (var y = (int)Math.Floor(cy - r); y <= (int)Math.Ceiling(cy + r); y++)
        {
            for (var x = (int)Math.Floor(cx - r); x <= (int)Math.Ceiling(cx + r); x++)
            {
                var dx = x + 0.5 - cx;
                var dy = y + 0.5 - cy;
                if ((dx * dx) + (dy * dy) <= r * r)
                {
                    c.Plot(x, y, premultiplied, StrokeCombine.Max);
                }
            }
        }
    }

    /// <summary>Aliased square of <paramref name="size"/> px centered on the pixel containing <paramref name="center"/>.</summary>
    public static void Square(StrokeCanvas c, Point center, int size, uint premultiplied)
    {
        ArgumentNullException.ThrowIfNull(c);
        var x0 = (int)Math.Floor(center.X) - ((size - 1) / 2);
        var y0 = (int)Math.Floor(center.Y) - ((size - 1) / 2);
        for (var y = y0; y < y0 + size; y++)
        {
            for (var x = x0; x < x0 + size; x++)
            {
                c.Plot(x, y, premultiplied, StrokeCombine.Max);
            }
        }

        c.Touch(new Imaging.PixelRect(x0, y0, size, size).Intersect(new Imaging.PixelRect(0, 0, c.Width, c.Height)));
    }

    /// <summary>Integer Bresenham line, invoking <paramref name="plot"/> for each pixel.</summary>
    public static void Bresenham(int x0, int y0, int x1, int y1, Action<int, int> plot)
    {
        ArgumentNullException.ThrowIfNull(plot);
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            plot(x0, y0);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>
    /// Antialiased polygon fill (non-zero winding) with 4 vertical sub-scanlines and exact horizontal coverage.
    /// </summary>
    public static void Polygon(StrokeCanvas c, IReadOnlyList<Point> pts, Color color, StrokeCombine combine, Func<int, int, double>? texture = null)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(pts);
        if (pts.Count < 3)
        {
            return;
        }

        var minY = (int)Math.Floor(pts.Min(p => p.Y));
        var maxY = (int)Math.Ceiling(pts.Max(p => p.Y));
        var minX = (int)Math.Floor(pts.Min(p => p.X));
        var maxX = (int)Math.Ceiling(pts.Max(p => p.X));
        minY = Math.Max(0, minY);
        maxY = Math.Min(c.Height, maxY);
        minX = Math.Max(0, minX);
        maxX = Math.Min(c.Width, maxX);
        if (minY >= maxY || minX >= maxX)
        {
            return;
        }

        const int sub = 4;
        var cover = new double[maxX - minX + 1];
        var crossings = new List<(double X, int Dir)>();
        for (var y = minY; y < maxY; y++)
        {
            Array.Clear(cover);
            for (var s = 0; s < sub; s++)
            {
                var sy = y + ((s + 0.5) / sub);
                crossings.Clear();
                for (var i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    if (a.Y == b.Y)
                    {
                        continue;
                    }

                    var dir = b.Y > a.Y ? 1 : -1;
                    var (lo, hi) = dir > 0 ? (a, b) : (b, a);
                    if (sy < lo.Y || sy >= hi.Y)
                    {
                        continue;
                    }

                    crossings.Add((lo.X + ((sy - lo.Y) * (hi.X - lo.X) / (hi.Y - lo.Y)), dir));
                }

                crossings.Sort((p, q) => p.X.CompareTo(q.X));
                var winding = 0;
                for (var i = 0; i < crossings.Count - 1; i++)
                {
                    winding += crossings[i].Dir;
                    if (winding != 0)
                    {
                        AddSpan(cover, minX, crossings[i].X, crossings[i + 1].X, 1.0 / sub);
                    }
                }
            }

            for (var x = minX; x < maxX; x++)
            {
                var cov = cover[x - minX];
                if (cov > 0)
                {
                    if (texture is not null)
                    {
                        cov *= texture(x, y);
                    }

                    c.PlotCoverage(x, y, color, cov, combine);
                }
            }
        }
    }

    /// <summary>Hash-based value noise in 0..1 (deterministic, used for paper/bristle textures).</summary>
    public static double Noise(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)((x * 374761393) + (y * 668265263) + (seed * 1442695041));
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535.0;
        }
    }

    private static void AddSpan(double[] cover, int minX, double x0, double x1, double weight)
    {
        if (x1 <= x0)
        {
            return;
        }

        x0 = Math.Max(x0, minX);
        x1 = Math.Min(x1, minX + cover.Length - 1);
        if (x1 <= x0)
        {
            return;
        }

        var i0 = (int)Math.Floor(x0);
        var i1 = (int)Math.Floor(x1);
        if (i0 == i1)
        {
            cover[i0 - minX] += (x1 - x0) * weight;
            return;
        }

        cover[i0 - minX] += (i0 + 1 - x0) * weight;
        for (var i = i0 + 1; i < i1; i++)
        {
            cover[i - minX] += weight;
        }

        if (i1 - minX < cover.Length)
        {
            cover[i1 - minX] += (x1 - i1) * weight;
        }
    }
}
