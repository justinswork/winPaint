using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WinPaint.Core.Imaging;

/// <summary>
/// Helpers for packed 32-bit pixels. Internal pixels are premultiplied BGRA (PBGRA32) packed into a
/// <see cref="uint"/> as <c>B | G &lt;&lt; 8 | R &lt;&lt; 16 | A &lt;&lt; 24</c> (byte order B,G,R,A in memory).
/// </summary>
public static class ColorUtil
{
    /// <summary>Fully transparent pixel.</summary>
    public const uint Transparent = 0;

    /// <summary>Opaque white.</summary>
    public const uint White = 0xFFFFFFFF;

    /// <summary>Opaque black.</summary>
    public const uint Black = 0xFF000000;

    /// <summary>Packs channels (already premultiplied) into a pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pack(byte a, byte r, byte g, byte b) => (uint)(b | (g << 8) | (r << 16) | (a << 24));

    /// <summary>Alpha channel of a packed pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte A(uint p) => (byte)(p >> 24);

    /// <summary>Red channel of a packed pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte R(uint p) => (byte)(p >> 16);

    /// <summary>Green channel of a packed pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte G(uint p) => (byte)(p >> 8);

    /// <summary>Blue channel of a packed pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte B(uint p) => (byte)p;

    /// <summary>Converts a straight-alpha color to a premultiplied packed pixel.</summary>
    public static uint FromColor(Color c) => Premultiply(c.A, c.R, c.G, c.B);

    /// <summary>Converts a straight-alpha color with an extra opacity factor (0..1) to a premultiplied pixel.</summary>
    public static uint FromColor(Color c, double opacity)
    {
        var a = (byte)Math.Clamp(Math.Round(c.A * opacity), 0, 255);
        return Premultiply(a, c.R, c.G, c.B);
    }

    /// <summary>Premultiplies straight channels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Premultiply(byte a, byte r, byte g, byte b)
    {
        if (a == 255)
        {
            return Pack(255, r, g, b);
        }

        if (a == 0)
        {
            return 0;
        }

        return Pack(a, Mul(r, a), Mul(g, a), Mul(b, a));
    }

    /// <summary>Converts a premultiplied pixel back to a straight-alpha color.</summary>
    public static Color ToColor(uint p)
    {
        var a = A(p);
        if (a == 0)
        {
            return Color.FromArgb(0, 0, 0, 0);
        }

        if (a == 255)
        {
            return Color.FromArgb(255, R(p), G(p), B(p));
        }

        return Color.FromArgb(a, Div(R(p), a), Div(G(p), a), Div(B(p), a));
    }

    /// <summary>Unpremultiplies a pixel to straight BGRA packed form.</summary>
    public static uint Unpremultiply(uint p)
    {
        var a = A(p);
        if (a == 255 || a == 0)
        {
            return a == 0 ? 0 : p;
        }

        return Pack(a, Div(R(p), a), Div(G(p), a), Div(B(p), a));
    }

    /// <summary>(x*y)/255 with rounding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Mul(int x, int y)
    {
        var t = (x * y) + 128;
        return (byte)((t + (t >> 8)) >> 8);
    }

    /// <summary>x*255/a with rounding, clamped.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Div(int x, int a) => (byte)Math.Min(255, ((x * 255) + (a / 2)) / a);

    /// <summary>Scales all channels of a premultiplied pixel by f/255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Scale(uint p, int f)
    {
        if (f >= 255)
        {
            return p;
        }

        if (f <= 0)
        {
            return 0;
        }

        return Pack(Mul(A(p), f), Mul(R(p), f), Mul(G(p), f), Mul(B(p), f));
    }

    /// <summary>Source-over compositing of premultiplied pixels: <c>src + dst*(1-srcA)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Over(uint src, uint dst)
    {
        var sa = src >> 24;
        if (sa == 255)
        {
            return src;
        }

        if (sa == 0)
        {
            return dst;
        }

        var inv = 255 - (int)sa;
        var a = (int)sa + Mul((int)(dst >> 24), inv);
        var r = (int)((src >> 16) & 0xFF) + Mul((int)((dst >> 16) & 0xFF), inv);
        var g = (int)((src >> 8) & 0xFF) + Mul((int)((dst >> 8) & 0xFF), inv);
        var b = (int)(src & 0xFF) + Mul((int)(dst & 0xFF), inv);
        return Pack((byte)Math.Min(a, 255), (byte)Math.Min(r, 255), (byte)Math.Min(g, 255), (byte)Math.Min(b, 255));
    }

    /// <summary>Inverts the color channels of a premultiplied pixel, keeping alpha.</summary>
    public static uint Invert(uint p)
    {
        var a = A(p);
        return Pack(a, (byte)(a - R(p)), (byte)(a - G(p)), (byte)(a - B(p)));
    }

    /// <summary>Inverts a straight color (alpha kept).</summary>
    public static Color Invert(Color c) => Color.FromArgb(c.A, (byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B));

    /// <summary>Luminance-threshold black-and-white conversion of a straight color (alpha kept).</summary>
    public static Color ToBlackWhite(Color c)
    {
        var lum = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
        var v = lum >= 128 ? (byte)255 : (byte)0;
        return Color.FromArgb(c.A, v, v, v);
    }

    /// <summary>Black-and-white conversion of a premultiplied pixel using the same threshold as <see cref="ToBlackWhite(Color)"/>.</summary>
    public static uint ToBlackWhite(uint p) => FromColor(ToBlackWhite(ToColor(p)));

    /// <summary>Formats a color as #RRGGBB.</summary>
    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Parses #RRGGBB, RRGGBB, #AARRGGBB or #RGB.</summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim().TrimStart('#');
        if (s.Length == 3)
        {
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        }

        if ((s.Length != 6 && s.Length != 8) || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        color = s.Length == 6
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}
