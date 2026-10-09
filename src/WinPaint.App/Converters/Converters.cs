using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPaint.App.Resources;
using WinPaint.Core.Brushes;
using WinPaint.Core.Imaging;
using WinPaint.Core.Shapes;

namespace WinPaint.App.Converters;

/// <summary>True when the bound enum value equals the parameter (for tool toggles).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>bool → Visibility (with optional "Invert" parameter).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter is "Invert")
        {
            b = !b;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Visibility.Visible;
}

/// <summary>Boolean negation (two-way).</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>null → Collapsed (parameter "Invert" flips).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null && value is not string { Length: 0 };
        if (parameter is "Invert")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Color (or null) → SolidColorBrush (transparent for null).</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = new SolidColorBrush(value is Color c ? c : Colors.Transparent);
        b.Freeze();
        return b;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SolidColorBrush b ? b.Color : Binding.DoNothing;
}

/// <summary>Enum value → localized display name (resource key "{Prefix}_{Value}").</summary>
public sealed class EnumNameConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var prefix = parameter as string ?? value.GetType().Name;
        var key = $"{prefix}_{value}";
        if (prefix == "Style" && value is ShapeStyle.None)
        {
            key = "Style_NoFill";
        }

        return Strings.ResourceManager.GetString(key, Strings.Culture) ?? value.ToString()!;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>ShapeKind → icon geometry (generated from the real shape geometry).</summary>
public sealed class ShapeIconConverter : IValueConverter
{
    private static readonly Dictionary<ShapeKind, Geometry> Cache = [];

    /// <summary>Icon geometry for a shape in a 16×16 box.</summary>
    public static Geometry For(ShapeKind kind)
    {
        if (Cache.TryGetValue(kind, out var g))
        {
            return g;
        }

        var box = new Rect(1, 2, 14, 12);
        g = kind switch
        {
            ShapeKind.Line => ShapeGeometry.Line(new Point(1, 15), new Point(15, 1)),
            ShapeKind.Curve => ShapeGeometry.Curve(new Point(1, 14), new Point(15, 2), new Point(4, -2), new Point(12, 18)),
            ShapeKind.Polygon => ShapeGeometry.FromPoints([new(3, 2), new(14, 5), new(10, 14), new(2, 11), new(5, 7)], true),
            _ => ShapeGeometry.Build(kind, kind is ShapeKind.Oval or ShapeKind.Heart ? new Rect(1, 1, 14, 14) : box),
        };
        Cache[kind] = g;
        return g;
    }

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ShapeKind k ? For(k) : Geometry.Empty;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>BrushKind → preview image of an S-stroke drawn with the real brush engine.</summary>
public sealed class BrushPreviewConverter : IValueConverter
{
    private static readonly Dictionary<BrushKind, BitmapSource> Cache = [];

    /// <summary>Preview bitmap for a brush.</summary>
    public static BitmapSource For(BrushKind kind)
    {
        if (Cache.TryGetValue(kind, out var b))
        {
            return b;
        }

        const int w = 48;
        const int h = 24;
        var canvas = new StrokeCanvas(w, h);
        var engine = BrushEngine.Create(kind, Color.FromRgb(0x30, 0x30, 0x30), kind is BrushKind.CalligraphyBrush or BrushKind.CalligraphyPen ? 7 : 6, 7);
        engine.Begin(canvas, new Point(4, 16));
        for (var i = 1; i <= 40; i++)
        {
            var t = i / 40.0;
            engine.MoveTo(canvas, new Point(4 + (t * 40), 12 - (Math.Sin(t * Math.PI * 2) * 6)));
            if (kind == BrushKind.Airbrush && i % 4 == 0)
            {
                engine.Tick(canvas);
            }
        }

        b = canvas.Surface.ToPixelBuffer().ToBitmapSource();
        Cache[kind] = b;
        return b;
    }

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BrushKind k ? For(k) : DependencyProperty.UnsetValue;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>ShapeStyle → whether it equals the parameter (for checkable menu items).</summary>
public sealed class StyleEqualsConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ShapeStyle s && parameter is ShapeStyle p && s == p;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Size (px) → preview dot diameter (capped).</summary>
public sealed class SizePreviewConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? Math.Clamp((double)i, 1, 40) : 1.0;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Percentage formatting ("{0:0}%").</summary>
public sealed class PercentConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? string.Format(culture, "{0:0.#}%", d) : string.Empty;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && double.TryParse(s.Trim().TrimEnd('%'), NumberStyles.Float, culture, out var d) ? d : Binding.DoNothing;
}

/// <summary>Full path → file name (for recent files).</summary>
public sealed class FileNameConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? System.IO.Path.GetFileName(s) : string.Empty;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
