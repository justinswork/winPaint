using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WinPaint.Core.Document;

namespace WinPaint.Core.Text;

/// <summary>
/// Builds the single <see cref="FormattedText"/> layout used both by the document renderer and the editor overlay,
/// so edit mode and committed mode share identical metrics.
/// </summary>
public static class TextLayoutEngine
{
    /// <summary>Minimum wrap width so FormattedText always has a positive width.</summary>
    public const double MinBoxWidth = 4;

    /// <summary>Creates the typeface for a text object.</summary>
    public static Typeface CreateTypeface(TextObject t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return new Typeface(
            new FontFamily(t.FontFamily),
            t.Italic ? FontStyles.Italic : FontStyles.Normal,
            t.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
    }

    /// <summary>Builds the formatted text for a text object (layout in untransformed canvas pixels).</summary>
    public static FormattedText Build(TextObject t, Brush? foreground = null)
    {
        ArgumentNullException.ThrowIfNull(t);
        var brush = foreground ?? CreateFrozenBrush(t.Foreground);
        var ft = new FormattedText(
            t.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            CreateTypeface(t),
            t.FontSizePx,
            brush,
            null,
            TextFormattingMode.Ideal,
            1.0)
        {
            MaxTextWidth = Math.Max(MinBoxWidth, t.Box.Width),
            Trimming = TextTrimming.None,
        };

        var decorations = new TextDecorationCollection();
        if (t.Underline)
        {
            decorations.Add(TextDecorations.Underline);
        }

        if (t.Strikethrough)
        {
            decorations.Add(TextDecorations.Strikethrough);
        }

        if (decorations.Count > 0)
        {
            ft.SetTextDecorations(decorations);
        }

        return ft;
    }

    /// <summary>Height of a single empty line for the object's font (used for empty boxes).</summary>
    public static double LineHeight(TextObject t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var probe = t.CloneText();
        probe.Text = "Ag";
        return Build(probe).Height;
    }

    /// <summary>
    /// The effective (auto-grown) box: the stored box, extended downward so the laid-out text always fits.
    /// </summary>
    public static Rect EffectiveBox(TextObject t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var textHeight = string.IsNullOrEmpty(t.Text) ? LineHeight(t) : Build(t).Height;
        if (t.Text.EndsWith('\n'))
        {
            textHeight += LineHeight(t);
        }

        return new Rect(t.Box.X, t.Box.Y, Math.Max(MinBoxWidth, t.Box.Width), Math.Max(t.Box.Height, textHeight));
    }

    /// <summary>Axis-aligned bounds of the transformed effective box in canvas pixels.</summary>
    public static Rect TransformedBounds(TextObject t)
    {
        var box = EffectiveBox(t);
        var r = box;
        r.Transform(t.Transform);
        return r;
    }

    /// <summary>True when canvas point <paramref name="p"/> falls inside the transformed box.</summary>
    public static bool HitTest(TextObject t, Point p)
    {
        ArgumentNullException.ThrowIfNull(t);
        var m = t.Transform;
        if (!m.HasInverse)
        {
            return false;
        }

        m.Invert();
        return EffectiveBox(t).Contains(m.Transform(p));
    }

    /// <summary>Creates a frozen solid brush.</summary>
    public static SolidColorBrush CreateFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
