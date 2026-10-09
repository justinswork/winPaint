using System.Runtime.CompilerServices;

namespace WinPaint.Core.Imaging;

/// <summary>Layer blend modes.</summary>
public enum BlendMode
{
    /// <summary>Source over.</summary>
    Normal,

    /// <summary>Cs × Cb.</summary>
    Multiply,

    /// <summary>Cs + Cb − Cs×Cb.</summary>
    Screen,

    /// <summary>HardLight with layers swapped.</summary>
    Overlay,

    /// <summary>min(Cs, Cb).</summary>
    Darken,

    /// <summary>max(Cs, Cb).</summary>
    Lighten,

    /// <summary>|Cs − Cb|.</summary>
    Difference,
}

/// <summary>Pixel blending of premultiplied pixels following the W3C compositing formulas.</summary>
public static class Blend
{
    /// <summary>Separable blend function on straight channel values in 0..1.</summary>
    public static double Apply(BlendMode mode, double cs, double cb) => mode switch
    {
        BlendMode.Multiply => cs * cb,
        BlendMode.Screen => cs + cb - (cs * cb),
        BlendMode.Overlay => cb <= 0.5 ? 2 * cs * cb : 1 - (2 * (1 - cs) * (1 - cb)),
        BlendMode.Darken => Math.Min(cs, cb),
        BlendMode.Lighten => Math.Max(cs, cb),
        BlendMode.Difference => Math.Abs(cs - cb),
        _ => cs,
    };

    /// <summary>
    /// Composites <paramref name="src"/> (with extra opacity 0..255) onto <paramref name="dst"/> using a blend mode:
    /// <c>co = cs(1−ab) + cb(1−as) + as·ab·B(Cs, Cb)</c> in premultiplied space.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pixel(BlendMode mode, uint src, uint dst, int opacity)
    {
        if (opacity < 255)
        {
            src = ColorUtil.Scale(src, opacity);
        }

        if (mode == BlendMode.Normal)
        {
            return ColorUtil.Over(src, dst);
        }

        var sa = ColorUtil.A(src);
        if (sa == 0)
        {
            return dst;
        }

        var da = ColorUtil.A(dst);
        if (da == 0)
        {
            return src;
        }

        double asf = sa / 255.0, abf = da / 255.0;
        var ao = asf + abf - (asf * abf);
        var r = Channel(mode, ColorUtil.R(src), ColorUtil.R(dst), asf, abf);
        var g = Channel(mode, ColorUtil.G(src), ColorUtil.G(dst), asf, abf);
        var b = Channel(mode, ColorUtil.B(src), ColorUtil.B(dst), asf, abf);
        return ColorUtil.Pack(ToByte(ao), ToByte(Math.Min(r, ao)), ToByte(Math.Min(g, ao)), ToByte(Math.Min(b, ao)));
    }

    /// <summary>Blends a row of pixels.</summary>
    public static void Row(BlendMode mode, ReadOnlySpan<uint> src, Span<uint> dst, int opacity)
    {
        if (mode == BlendMode.Normal && opacity >= 255)
        {
            for (var i = 0; i < src.Length; i++)
            {
                var s = src[i];
                if (s >= 0xFF000000)
                {
                    dst[i] = s;
                }
                else if (s != 0)
                {
                    dst[i] = ColorUtil.Over(s, dst[i]);
                }
            }

            return;
        }

        for (var i = 0; i < src.Length; i++)
        {
            if (src[i] != 0)
            {
                dst[i] = Pixel(mode, src[i], dst[i], opacity);
            }
        }
    }

    private static double Channel(BlendMode mode, byte csP, byte cbP, double asf, double abf)
    {
        var csPre = csP / 255.0;
        var cbPre = cbP / 255.0;
        var cs = csPre / asf;
        var cb = cbPre / abf;
        return (csPre * (1 - abf)) + (cbPre * (1 - asf)) + (asf * abf * Apply(mode, Math.Min(cs, 1), Math.Min(cb, 1)));
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255.0), 0, 255);
}
