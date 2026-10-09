namespace WinPaint.Core.Imaging;

/// <summary>Resampling filters.</summary>
public enum ResampleMode
{
    /// <summary>Nearest neighbor (sharp, for pixel art).</summary>
    NearestNeighbor,

    /// <summary>Bicubic (Catmull-Rom) widened for downscaling (area-correct like Fant).</summary>
    HighQuality,
}

/// <summary>Image resizing on premultiplied pixels.</summary>
public static class Resampler
{
    /// <summary>
    /// Resizes using nearest neighbor for exact integer upscales (keeps pixel art sharp) and high quality otherwise.
    /// </summary>
    public static PixelBuffer ResizeAuto(PixelBuffer src, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (width == src.Width && height == src.Height)
        {
            return src.Clone();
        }

        var integerUp = width >= src.Width && height >= src.Height && width % src.Width == 0 && height % src.Height == 0;
        return Resize(src, width, height, integerUp ? ResampleMode.NearestNeighbor : ResampleMode.HighQuality);
    }

    /// <summary>Resizes to the given size.</summary>
    public static PixelBuffer Resize(PixelBuffer src, int width, int height, ResampleMode mode)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width == src.Width && height == src.Height)
        {
            return src.Clone();
        }

        return mode == ResampleMode.NearestNeighbor ? Nearest(src, width, height) : Cubic(src, width, height);
    }

    private static PixelBuffer Nearest(PixelBuffer src, int width, int height)
    {
        var dst = new PixelBuffer(width, height);
        var xmap = new int[width];
        for (var x = 0; x < width; x++)
        {
            xmap[x] = Math.Min(src.Width - 1, (int)(((x + 0.5) * src.Width) / width));
        }

        Parallel.For(0, height, y =>
        {
            var sy = Math.Min(src.Height - 1, (int)(((y + 0.5) * src.Height) / height));
            var srow = sy * src.Width;
            var drow = y * width;
            for (var x = 0; x < width; x++)
            {
                dst.Pixels[drow + x] = src.Pixels[srow + xmap[x]];
            }
        });
        return dst;
    }

    private static PixelBuffer Cubic(PixelBuffer src, int width, int height)
    {
        var hw = Weights(src.Width, width);
        var vw = Weights(src.Height, height);

        // Horizontal pass: src.Height rows of `width` pixels.
        var mid = new uint[(long)width * src.Height];
        Parallel.For(0, src.Height, y =>
        {
            var srow = y * src.Width;
            for (var x = 0; x < width; x++)
            {
                var (start, w) = hw[x];
                double a = 0, r = 0, g = 0, b = 0;
                for (var i = 0; i < w.Length; i++)
                {
                    var p = src.Pixels[srow + start + i];
                    var k = w[i];
                    a += (p >> 24) * k;
                    r += ((p >> 16) & 0xFF) * k;
                    g += ((p >> 8) & 0xFF) * k;
                    b += (p & 0xFF) * k;
                }

                mid[((long)y * width) + x] = PackClamped(a, r, g, b);
            }
        });

        var dst = new PixelBuffer(width, height);
        Parallel.For(0, height, y =>
        {
            var (start, w) = vw[y];
            for (var x = 0; x < width; x++)
            {
                double a = 0, r = 0, g = 0, b = 0;
                for (var i = 0; i < w.Length; i++)
                {
                    var p = mid[((long)(start + i) * width) + x];
                    var k = w[i];
                    a += (p >> 24) * k;
                    r += ((p >> 16) & 0xFF) * k;
                    g += ((p >> 8) & 0xFF) * k;
                    b += (p & 0xFF) * k;
                }

                dst.Pixels[(y * width) + x] = PackClamped(a, r, g, b);
            }
        });
        return dst;
    }

    private static uint PackClamped(double a, double r, double g, double b)
    {
        var ai = (int)Math.Clamp(Math.Round(a), 0, 255);
        var ri = (int)Math.Clamp(Math.Round(r), 0, ai);
        var gi = (int)Math.Clamp(Math.Round(g), 0, ai);
        var bi = (int)Math.Clamp(Math.Round(b), 0, ai);
        return ColorUtil.Pack((byte)ai, (byte)ri, (byte)gi, (byte)bi);
    }

    private static (int Start, double[] W)[] Weights(int srcSize, int dstSize)
    {
        var scale = (double)dstSize / srcSize;
        var filterScale = Math.Max(1.0, 1.0 / scale);
        var support = 2.0 * filterScale;
        var result = new (int, double[])[dstSize];
        for (var i = 0; i < dstSize; i++)
        {
            var center = ((i + 0.5) / scale) - 0.5;
            var start = (int)Math.Floor(center - support) + 1;
            var end = (int)Math.Floor(center + support);
            start = Math.Max(0, start);
            end = Math.Min(srcSize - 1, end);
            if (end < start)
            {
                end = start = Math.Clamp((int)Math.Round(center), 0, srcSize - 1);
            }

            var w = new double[end - start + 1];
            double sum = 0;
            for (var j = start; j <= end; j++)
            {
                var v = CatmullRom((j - center) / filterScale);
                w[j - start] = v;
                sum += v;
            }

            if (Math.Abs(sum) < 1e-9)
            {
                Array.Fill(w, 1.0 / w.Length);
            }
            else
            {
                for (var k = 0; k < w.Length; k++)
                {
                    w[k] /= sum;
                }
            }

            result[i] = (start, w);
        }

        return result;
    }

    private static double CatmullRom(double x)
    {
        x = Math.Abs(x);
        const double a = -0.5;
        if (x < 1)
        {
            return (((a + 2) * x) - (a + 3)) * x * x + 1;
        }

        if (x < 2)
        {
            return (((((a * x) - (5 * a)) * x) + (8 * a)) * x) - (4 * a);
        }

        return 0;
    }
}
