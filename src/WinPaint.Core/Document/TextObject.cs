using System.Windows;
using System.Windows.Media;

namespace WinPaint.Core.Document;

/// <summary>
/// A live, re-editable text element. Lives only in memory; it is flattened into pixels when the document is saved.
/// </summary>
public sealed class TextObject : LayerElement
{
    /// <summary>Plain text, may contain line breaks.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Layout box in untransformed text space (canvas px). Width is the wrap width.</summary>
    public Rect Box { get; set; }

    /// <summary>Accumulated whole-image transforms (identity at creation).</summary>
    public Matrix Transform { get; set; } = Matrix.Identity;

    /// <summary>Font family name.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Font size in points (1–999). Rendered at 96 px per inch.</summary>
    public double FontSizePt { get; set; } = 11;

    /// <summary>Bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Underline.</summary>
    public bool Underline { get; set; }

    /// <summary>Strikethrough.</summary>
    public bool Strikethrough { get; set; }

    /// <summary>Text color.</summary>
    public Color Foreground { get; set; } = Colors.Black;

    /// <summary>Background color (used when <see cref="OpaqueBackground"/>).</summary>
    public Color Background { get; set; } = Colors.White;

    /// <summary>Fill the box with <see cref="Background"/>.</summary>
    public bool OpaqueBackground { get; set; }

    /// <summary>Overall opacity 0..1.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Font size converted to canvas pixels.</summary>
    public double FontSizePx => FontSizePt * 96.0 / 72.0;

    /// <inheritdoc/>
    public override LayerElement CloneElement() => CloneText();

    /// <summary>Typed deep copy keeping the same id.</summary>
    public TextObject CloneText() => CopyTo(new TextObject { Id = Id });

    /// <summary>Copy with a new id (for layer duplication).</summary>
    public TextObject CloneWithNewId() => CopyTo(new TextObject());

    /// <summary>Copies every property except <see cref="LayerElement.Id"/> from another object.</summary>
    public void CopyFrom(TextObject other)
    {
        ArgumentNullException.ThrowIfNull(other);
        other.CopyTo(this);
    }

    /// <summary>True when every property (including id) matches.</summary>
    public bool StateEquals(TextObject? o) =>
        o is not null && o.Id == Id && o.Text == Text && o.Box == Box && o.Transform == Transform && o.FontFamily == FontFamily
        && o.FontSizePt.Equals(FontSizePt) && o.Bold == Bold && o.Italic == Italic && o.Underline == Underline
        && o.Strikethrough == Strikethrough && o.Foreground == Foreground && o.Background == Background
        && o.OpaqueBackground == OpaqueBackground && o.Opacity.Equals(Opacity);

    /// <summary>A key that changes whenever the rendered appearance changes.</summary>
    public string RenderKey() => string.Join(
        '|',
        Text,
        Box.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Transform.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FontFamily,
        FontSizePt.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        Bold,
        Italic,
        Underline,
        Strikethrough,
        Foreground,
        Background,
        OpaqueBackground,
        Opacity.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    private TextObject CopyTo(TextObject t)
    {
        t.Text = Text;
        t.Box = Box;
        t.Transform = Transform;
        t.FontFamily = FontFamily;
        t.FontSizePt = FontSizePt;
        t.Bold = Bold;
        t.Italic = Italic;
        t.Underline = Underline;
        t.Strikethrough = Strikethrough;
        t.Foreground = Foreground;
        t.Background = Background;
        t.OpaqueBackground = OpaqueBackground;
        t.Opacity = Opacity;
        return t;
    }
}
