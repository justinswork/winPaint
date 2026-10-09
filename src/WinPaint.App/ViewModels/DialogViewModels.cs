using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Imaging;

namespace WinPaint.App.ViewModels;

/// <summary>Resize and skew dialog state.</summary>
public sealed partial class ResizeSkewViewModel : ObservableObject
{
    private bool _updating;

    /// <summary>Creates the view model for an image of the given size.</summary>
    public ResizeSkewViewModel(int width, int height)
    {
        OriginalWidth = Math.Max(1, width);
        OriginalHeight = Math.Max(1, height);
        HorizontalValue = 100;
        VerticalValue = 100;
    }

    /// <summary>Source width.</summary>
    public int OriginalWidth { get; }

    /// <summary>Source height.</summary>
    public int OriginalHeight { get; }

    /// <summary>Resize by percentage (otherwise pixels).</summary>
    [ObservableProperty]
    public partial bool ByPercentage { get; set; } = true;

    /// <summary>Horizontal value (percent or pixels).</summary>
    [ObservableProperty]
    public partial double HorizontalValue { get; set; }

    /// <summary>Vertical value (percent or pixels).</summary>
    [ObservableProperty]
    public partial double VerticalValue { get; set; }

    /// <summary>Keep the aspect ratio.</summary>
    [ObservableProperty]
    public partial bool KeepAspect { get; set; } = true;

    /// <summary>Horizontal skew in degrees (−89…89).</summary>
    [ObservableProperty]
    public partial double SkewHorizontal { get; set; }

    /// <summary>Vertical skew in degrees (−89…89).</summary>
    [ObservableProperty]
    public partial double SkewVertical { get; set; }

    /// <summary>Resulting width in pixels.</summary>
    public int ResultWidth => Math.Clamp((int)Math.Round(ByPercentage ? OriginalWidth * HorizontalValue / 100.0 : HorizontalValue), 1, 100000);

    /// <summary>Resulting height in pixels.</summary>
    public int ResultHeight => Math.Clamp((int)Math.Round(ByPercentage ? OriginalHeight * VerticalValue / 100.0 : VerticalValue), 1, 100000);

    /// <summary>True when the values are valid.</summary>
    public bool IsValid => HorizontalValue > 0 && VerticalValue > 0 && Math.Abs(SkewHorizontal) <= 89 && Math.Abs(SkewVertical) <= 89;

    /// <summary>Dialog result.</summary>
    public ResizeSkewResult Result => new(ResultWidth, ResultHeight, Math.Clamp(SkewHorizontal, -89, 89), Math.Clamp(SkewVertical, -89, 89));

    partial void OnByPercentageChanged(bool value)
    {
        _updating = true;
        if (value)
        {
            HorizontalValue = Math.Round(HorizontalValue * 100.0 / OriginalWidth, 1);
            VerticalValue = Math.Round(VerticalValue * 100.0 / OriginalHeight, 1);
        }
        else
        {
            HorizontalValue = Math.Round(OriginalWidth * HorizontalValue / 100.0);
            VerticalValue = Math.Round(OriginalHeight * VerticalValue / 100.0);
        }

        _updating = false;
    }

    partial void OnHorizontalValueChanged(double value)
    {
        if (_updating || !KeepAspect)
        {
            return;
        }

        _updating = true;
        VerticalValue = ByPercentage ? value : Math.Round(value * OriginalHeight / OriginalWidth);
        _updating = false;
    }

    partial void OnVerticalValueChanged(double value)
    {
        if (_updating || !KeepAspect)
        {
            return;
        }

        _updating = true;
        HorizontalValue = ByPercentage ? value : Math.Round(value * OriginalWidth / OriginalHeight);
        _updating = false;
    }
}

/// <summary>Measurement units in Image properties.</summary>
public enum SizeUnit
{
    /// <summary>Inches.</summary>
    Inches,

    /// <summary>Centimeters.</summary>
    Centimeters,

    /// <summary>Pixels.</summary>
    Pixels,
}

/// <summary>Image properties dialog state.</summary>
public sealed partial class ImagePropertiesViewModel : ObservableObject
{
    private readonly ImagePropertiesInfo _info;
    private bool _updating;
    private double _widthPx;
    private double _heightPx;

    /// <summary>Creates the view model.</summary>
    public ImagePropertiesViewModel(ImagePropertiesInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _info = info;
        _widthPx = info.Width;
        _heightPx = info.Height;
        Unit = SizeUnit.Pixels;
        WidthValue = info.Width;
        HeightValue = info.Height;
    }

    /// <summary>Last saved text.</summary>
    public string LastSavedText => _info.LastSaved?.ToString("g", CultureInfo.CurrentCulture) ?? Strings.Dlg_NotAvailable;

    /// <summary>Size on disk text.</summary>
    public string SizeOnDiskText => _info.SizeOnDisk is { } s ? MainViewModel.FormatBytes(s) : Strings.Dlg_NotAvailable;

    /// <summary>Resolution text.</summary>
    public string ResolutionText => string.Format(CultureInfo.CurrentCulture, Strings.Dlg_DpiFormat, Math.Round(_info.DpiX));

    /// <summary>Units.</summary>
    [ObservableProperty]
    public partial SizeUnit Unit { get; set; }

    /// <summary>Width in the current unit.</summary>
    [ObservableProperty]
    public partial double WidthValue { get; set; }

    /// <summary>Height in the current unit.</summary>
    [ObservableProperty]
    public partial double HeightValue { get; set; }

    /// <summary>Convert to black and white.</summary>
    [ObservableProperty]
    public partial bool BlackAndWhite { get; set; }

    /// <summary>Unit radio helpers.</summary>
    public bool IsInches
    {
        get => Unit == SizeUnit.Inches;
        set
        {
            if (value)
            {
                Unit = SizeUnit.Inches;
            }
        }
    }

    /// <summary>Unit radio helpers.</summary>
    public bool IsCentimeters
    {
        get => Unit == SizeUnit.Centimeters;
        set
        {
            if (value)
            {
                Unit = SizeUnit.Centimeters;
            }
        }
    }

    /// <summary>Unit radio helpers.</summary>
    public bool IsPixels
    {
        get => Unit == SizeUnit.Pixels;
        set
        {
            if (value)
            {
                Unit = SizeUnit.Pixels;
            }
        }
    }

    /// <summary>Resulting pixel width.</summary>
    public int ResultWidth => Math.Clamp((int)Math.Round(_widthPx), 1, 100000);

    /// <summary>Resulting pixel height.</summary>
    public int ResultHeight => Math.Clamp((int)Math.Round(_heightPx), 1, 100000);

    /// <summary>Restores the defaults (current size, pixels, color).</summary>
    public void ResetDefaults()
    {
        Unit = SizeUnit.Pixels;
        _widthPx = _info.Width;
        _heightPx = _info.Height;
        Show();
        BlackAndWhite = false;
    }

    partial void OnUnitChanged(SizeUnit value)
    {
        OnPropertyChanged(nameof(IsInches));
        OnPropertyChanged(nameof(IsCentimeters));
        OnPropertyChanged(nameof(IsPixels));
        Show();
    }

    partial void OnWidthValueChanged(double value)
    {
        if (!_updating)
        {
            _widthPx = ToPx(value, _info.DpiX);
        }
    }

    partial void OnHeightValueChanged(double value)
    {
        if (!_updating)
        {
            _heightPx = ToPx(value, _info.DpiY);
        }
    }

    private void Show()
    {
        _updating = true;
        WidthValue = FromPx(_widthPx, _info.DpiX);
        HeightValue = FromPx(_heightPx, _info.DpiY);
        _updating = false;
    }

    private double ToPx(double v, double dpi) => Unit switch
    {
        SizeUnit.Inches => v * dpi,
        SizeUnit.Centimeters => v * dpi / 2.54,
        _ => v,
    };

    private double FromPx(double px, double dpi) => Unit switch
    {
        SizeUnit.Inches => Math.Round(px / dpi, 2),
        SizeUnit.Centimeters => Math.Round(px * 2.54 / dpi, 2),
        _ => Math.Round(px),
    };
}

/// <summary>Edit colors dialog state: HSV square + value slider, RGB, HSV, hex and alpha, kept in sync.</summary>
public sealed partial class EditColorsViewModel : ObservableObject
{
    private bool _updating;

    /// <summary>Creates the view model.</summary>
    public EditColorsViewModel(Color initial)
    {
        OldColor = initial;
        SetFromColor(initial);
    }

    /// <summary>Color before editing.</summary>
    public Color OldColor { get; }

    /// <summary>Red 0–255.</summary>
    [ObservableProperty]
    public partial int Red { get; set; }

    /// <summary>Green 0–255.</summary>
    [ObservableProperty]
    public partial int Green { get; set; }

    /// <summary>Blue 0–255.</summary>
    [ObservableProperty]
    public partial int Blue { get; set; }

    /// <summary>Alpha 0–255.</summary>
    [ObservableProperty]
    public partial int Alpha { get; set; } = 255;

    /// <summary>Hue 0–359.</summary>
    [ObservableProperty]
    public partial double Hue { get; set; }

    /// <summary>Saturation 0–100.</summary>
    [ObservableProperty]
    public partial double Saturation { get; set; }

    /// <summary>Value 0–100.</summary>
    [ObservableProperty]
    public partial double Value { get; set; }

    /// <summary>Hex #RRGGBB.</summary>
    [ObservableProperty]
    public partial string Hex { get; set; } = "#000000";

    /// <summary>The edited color.</summary>
    public Color NewColor => Color.FromArgb((byte)Alpha, (byte)Red, (byte)Green, (byte)Blue);

    /// <summary>Pure hue color (for the saturation/value square background).</summary>
    public Color HueColor => HsvToColor(Hue, 100, 100);

    /// <summary>Sets saturation/value from a position in the square (0..1 each).</summary>
    public void PickSquare(double x01, double y01)
    {
        Saturation = Math.Round(Math.Clamp(x01, 0, 1) * 100, 1);
        Value = Math.Round((1 - Math.Clamp(y01, 0, 1)) * 100, 1);
    }

    /// <summary>Sets the hue from a position on the hue bar (0..1).</summary>
    public void PickHue(double y01) => Hue = Math.Round(Math.Clamp(y01, 0, 1) * 359, 1);

    partial void OnRedChanged(int value) => FromRgb();

    partial void OnGreenChanged(int value) => FromRgb();

    partial void OnBlueChanged(int value) => FromRgb();

    partial void OnAlphaChanged(int value) => OnPropertyChanged(nameof(NewColor));

    partial void OnHueChanged(double value) => FromHsv();

    partial void OnSaturationChanged(double value) => FromHsv();

    partial void OnValueChanged(double value) => FromHsv();

    partial void OnHexChanged(string value)
    {
        if (_updating || !ColorUtil.TryParseHex(value, out var c))
        {
            return;
        }

        _updating = true;
        Red = c.R;
        Green = c.G;
        Blue = c.B;
        if (value.Trim().TrimStart('#').Length == 8)
        {
            Alpha = c.A;
        }

        SetHsvFrom(Color.FromRgb(c.R, c.G, c.B));
        _updating = false;
        Notify();
    }

    private void SetFromColor(Color c)
    {
        _updating = true;
        Red = c.R;
        Green = c.G;
        Blue = c.B;
        Alpha = c.A;
        SetHsvFrom(c);
        Hex = ColorUtil.ToHex(c);
        _updating = false;
        Notify();
    }

    private void FromRgb()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        Red = Math.Clamp(Red, 0, 255);
        Green = Math.Clamp(Green, 0, 255);
        Blue = Math.Clamp(Blue, 0, 255);
        SetHsvFrom(Color.FromRgb((byte)Red, (byte)Green, (byte)Blue));
        Hex = ColorUtil.ToHex(NewColor);
        _updating = false;
        Notify();
    }

    private void FromHsv()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        var c = HsvToColor(Hue, Saturation, Value);
        Red = c.R;
        Green = c.G;
        Blue = c.B;
        Hex = ColorUtil.ToHex(c);
        _updating = false;
        Notify();
    }

    private void SetHsvFrom(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * (((b - r) / d) + 2) : 60 * (((r - g) / d) + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        Hue = Math.Round(h, 1);
        Saturation = Math.Round(max == 0 ? 0 : d / max * 100, 1);
        Value = Math.Round(max * 100, 1);
    }

    /// <summary>HSV (h 0–360, s/v 0–100) to color.</summary>
    public static Color HsvToColor(double h, double s, double v)
    {
        s = Math.Clamp(s, 0, 100) / 100;
        v = Math.Clamp(v, 0, 100) / 100;
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(NewColor));
        OnPropertyChanged(nameof(HueColor));
    }
}
