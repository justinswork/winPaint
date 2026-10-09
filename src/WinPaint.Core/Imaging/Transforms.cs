using System.Windows.Media;

namespace WinPaint.Core.Imaging;

/// <summary>Orthogonal rotations and flips.</summary>
public enum OrthoTransform
{
    /// <summary>Rotate 90° clockwise.</summary>
    RotateRight,

    /// <summary>Rotate 90° counter-clockwise.</summary>
    RotateLeft,

    /// <summary>Rotate 180°.</summary>
    Rotate180,

    /// <summary>Mirror left-right.</summary>
    FlipHorizontal,

    /// <summary>Mirror top-bottom.</summary>
    FlipVertical,
}

/// <summary>Pixel-exact geometric transforms of dense buffers.</summary>
public static class Transforms
{
    /// <summary>Size after an orthogonal transform.</summary>
    public static (int W, int H) SizeAfter(OrthoTransform t, int w, int h) =>
        t is OrthoTransform.RotateLeft or OrthoTransform.RotateRight ? (h, w) : (w, h);

    /// <summary>
    /// Continuous-coordinate matrix of an orthogonal transform for an image of size w×h (maps the canvas rectangle
    /// onto the new canvas rectangle). Used to update text objects.
    /// </summary>
    public static Matrix MatrixFor(OrthoTransform t, int w, int h) => t switch
    {
        OrthoTransform.RotateRight => new Matrix(0, 1, -1, 0, h, 0),
        OrthoTransform.RotateLeft => new Matrix(0, -1, 1, 0, 0, w),
        OrthoTransform.Rotate180 => new Matrix(-1, 0, 0, -1, w, h),
        OrthoTransform.FlipHorizontal => new Matrix(-1, 0, 0, 1, w, 0),
        _ => new Matrix(1, 0, 0, -1, 0, h),
    };

    /// <summary>Applies an orthogonal transform (exact, lossless).</summary>
    public static PixelBuffer Apply(PixelBuffer src, OrthoTransform t)
    {
        ArgumentNullException.ThrowIfNull(src);
        var w = src.Width;
        var h = src.Height;
        var (nw, nh) = SizeAfter(t, w, h);
        var dst = new PixelBuffer(nw, nh);
        var s = src.Pixels;
        var d = dst.Pixels;
        Parallel.For(0, h, y =>
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                int dx, dy;
                switch (t)
                {
                    case OrthoTransform.RotateRight:
                        dx = h - 1 - y;
                        dy = x;
                        break;
                    case OrthoTransform.RotateLeft:
                        dx = y;
                        dy = w - 1 - x;
                        break;
                    case OrthoTransform.Rotate180:
                        dx = w - 1 - x;
                        dy = h - 1 - y;
                        break;
                    case OrthoTransform.FlipHorizontal:
                        dx = w - 1 - x;
                        dy = y;
                        break;
                    default:
                        dx = x;
                        dy = h - 1 - y;
                        break;
                }

                d[(dy * nw) + dx] = s[row + x];
            }
        });
        return dst;
    }

    /// <summary>Skew geometry: matrix and output size for horizontal/vertical skew angles (degrees, −89…89).</summary>
    public static (Matrix Matrix, int Width, int Height) SkewGeometry(int w, int h, double horizontalDeg, double verticalDeg)
    {
        var th = Math.Tan(Math.Clamp(horizontalDeg, -89, 89) * Math.PI / 180);
        var tv = Math.Tan(Math.Clamp(verticalDeg, -89, 89) * Math.PI / 180);

        // x' = x + y·th, y' = y + x·tv, then shift so the result starts at 0.
        var m = new Matrix(1, tv, th, 1, 0, 0);
        System.Windows.Point[] corners = [new(0, 0), new(w, 0), new(0, h), new(w, h)];
        var tc = corners.Select(m.Transform).ToArray();
        var minX = tc.Min(p => p.X);
        var minY = tc.Min(p => p.Y);
        var maxX = tc.Max(p => p.X);
        var maxY = tc.Max(p => p.Y);
        m.Translate(-minX, -minY);
        var nw = Math.Max(1, (int)Math.Ceiling(maxX - minX - 1e-9));
        var nh = Math.Max(1, (int)Math.Ceiling(maxY - minY - 1e-9));
        return (m, nw, nh);
    }

    /// <summary>
    /// Resamples <paramref name="src"/> through an affine matrix into a new buffer (bilinear on premultiplied pixels;
    /// areas outside the source become <paramref name="fill"/>).
    /// </summary>
    public static PixelBuffer Affine(PixelBuffer src, Matrix m, int width, int height, uint fill = 0)
    {
        ArgumentNullException.ThrowIfNull(src);
        var inv = m;
        inv.Invert();
        var dst = new PixelBuffer(width, height);
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var p = inv.Transform(new System.Windows.Point(x + 0.5, y + 0.5));
                dst.Pixels[(y * width) + x] = SampleBilinear(src, p.X - 0.5, p.Y - 0.5, fill);
            }
        });
        return dst;
    }

    private static uint SampleBilinear(PixelBuffer s, double fx, double fy, uint fill)
    {
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var ax = fx - x0;
        var ay = fy - y0;
        if (x0 < -1 || y0 < -1 || x0 >= s.Width || y0 >= s.Height)
        {
            return fill;
        }

        uint P(int x, int y) => x < 0 || y < 0 || x >= s.Width || y >= s.Height ? fill : s.Pixels[(y * s.Width) + x];
        var p00 = P(x0, y0);
        var p10 = P(x0 + 1, y0);
        var p01 = P(x0, y0 + 1);
        var p11 = P(x0 + 1, y0 + 1);
        if (p00 == p10 && p00 == p01 && p00 == p11)
        {
            return p00;
        }

        double w00 = (1 - ax) * (1 - ay), w10 = ax * (1 - ay), w01 = (1 - ax) * ay, w11 = ax * ay;
        byte Ch(int shift) => (byte)Math.Clamp(Math.Round(
            (((p00 >> shift) & 0xFF) * w00) + (((p10 >> shift) & 0xFF) * w10) + (((p01 >> shift) & 0xFF) * w01) + (((p11 >> shift) & 0xFF) * w11)), 0, 255);
        return ColorUtil.Pack(Ch(24), Ch(16), Ch(8), Ch(0));
    }

    /// <summary>Inverts every pixel (premultiplied inversion distributes over source-over compositing).</summary>
    public static void Invert(PixelBuffer b, Func<int, int, bool>? mask = null)
    {
        ArgumentNullException.ThrowIfNull(b);
        for (var y = 0; y < b.Height; y++)
        {
            for (var x = 0; x < b.Width; x++)
            {
                if (mask is null || mask(x, y))
                {
                    var i = (y * b.Width) + x;
                    b.Pixels[i] = ColorUtil.Invert(b.Pixels[i]);
                }
            }
        }
    }
}
