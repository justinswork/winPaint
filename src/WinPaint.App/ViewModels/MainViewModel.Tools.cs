using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Brushes;
using WinPaint.Core.Imaging;
using WinPaint.Core.Shapes;
using WinPaint.Core.Tools;

namespace WinPaint.App.ViewModels;

/// <summary>A palette swatch.</summary>
public sealed partial class Swatch : ObservableObject
{
    /// <summary>Creates a swatch.</summary>
    public Swatch(Color? color, string name)
    {
        Color = color;
        Name = name;
    }

    /// <summary>The color (null = empty custom slot).</summary>
    [ObservableProperty]
    public partial Color? Color { get; set; }

    /// <summary>Accessible name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }
}

/// <summary>A shape gallery entry.</summary>
public sealed partial class ShapeItem(ShapeKind kind) : ObservableObject
{
    /// <summary>The shape.</summary>
    public ShapeKind Kind { get; } = kind;

    /// <summary>True when this shape is the active drawing shape.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

/// <summary>Tool selection, colors, sizes, brush/shape choices and text formatting.</summary>
public sealed partial class MainViewModel
{
    private ITool _activeTool;
    private ToolKind _previousTool = ToolKind.Pencil;
    private Point? _textCaretRequest;
    private bool _loadingTextProps;

    /// <summary>The 20 default palette colors (Paint's two rows).</summary>
    public static IReadOnlyList<Color> DefaultPalette { get; } =
    [
        C(0x000000), C(0x7F7F7F), C(0x880015), C(0xED1C24), C(0xFF7F27), C(0xFFF200), C(0x22B14C), C(0x00A2E8), C(0x3F48CC), C(0xA349A4),
        C(0xFFFFFF), C(0xC3C3C3), C(0xB97A57), C(0xFFAEC9), C(0xFFC90E), C(0xEFE4B0), C(0xB5E61D), C(0x99D9EA), C(0x7092BE), C(0xC8BFE7),
    ];

    /// <summary>Palette swatches.</summary>
    public IReadOnlyList<Swatch> Palette { get; } = DefaultPalette
        .Select((c, i) => new Swatch(c, string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Color_Palette, i + 1)))
        .ToList();

    /// <summary>The 10 custom color slots.</summary>
    public ObservableCollection<Swatch> CustomColors { get; } = [];

    /// <summary>All brushes, for the Brushes dropdown.</summary>
    public static IReadOnlyList<BrushKind> AllBrushes { get; } = Enum.GetValues<BrushKind>();

    /// <summary>All shapes, for the Shapes gallery.</summary>
    public static IReadOnlyList<ShapeKind> AllShapes { get; } = Enum.GetValues<ShapeKind>();

    /// <summary>Shape gallery entries.</summary>
    public IReadOnlyList<ShapeItem> ShapeItems { get; } = Enum.GetValues<ShapeKind>().Select(k => new ShapeItem(k)).ToList();

    /// <summary>All outline/fill styles.</summary>
    public static IReadOnlyList<ShapeStyle> AllStyles { get; } = Enum.GetValues<ShapeStyle>();

    /// <summary>Active tool.</summary>
    [ObservableProperty]
    public partial ToolKind ActiveToolKind { get; set; } = ToolKind.Pencil;

    /// <summary>Color 1.</summary>
    [ObservableProperty]
    public partial Color PrimaryColor { get; set; } = Colors.Black;

    /// <summary>Color 2.</summary>
    [ObservableProperty]
    public partial Color SecondaryColor { get; set; } = Colors.White;

    /// <summary>Which color slot palette clicks set (1 or 2).</summary>
    [ObservableProperty]
    public partial int ActiveColorSlot { get; set; } = 1;

    /// <summary>Current brush.</summary>
    [ObservableProperty]
    public partial BrushKind BrushKind { get; set; }

    /// <summary>Current shape.</summary>
    [ObservableProperty]
    public partial ShapeKind ShapeKind { get; set; } = ShapeKind.Rectangle;

    /// <summary>Shape outline style.</summary>
    [ObservableProperty]
    public partial ShapeStyle OutlineStyle { get; set; } = ShapeStyle.Solid;

    /// <summary>Shape fill style.</summary>
    [ObservableProperty]
    public partial ShapeStyle FillStyle { get; set; } = ShapeStyle.None;

    /// <summary>Opacity percent (1–100).</summary>
    [ObservableProperty]
    public partial double OpacityPercent { get; set; } = 100;

    /// <summary>Transparent selection toggle.</summary>
    [ObservableProperty]
    public partial bool TransparentSelection { get; set; }

    /// <summary>Text: font family.</summary>
    [ObservableProperty]
    public partial string TextFontFamily { get; set; } = "Segoe UI";

    /// <summary>Text: font size (pt).</summary>
    [ObservableProperty]
    public partial double TextFontSize { get; set; } = 11;

    /// <summary>Text: bold.</summary>
    [ObservableProperty]
    public partial bool TextBold { get; set; }

    /// <summary>Text: italic.</summary>
    [ObservableProperty]
    public partial bool TextItalic { get; set; }

    /// <summary>Text: underline.</summary>
    [ObservableProperty]
    public partial bool TextUnderline { get; set; }

    /// <summary>Text: strikethrough.</summary>
    [ObservableProperty]
    public partial bool TextStrikethrough { get; set; }

    /// <summary>Text: opaque background.</summary>
    [ObservableProperty]
    public partial bool TextOpaque { get; set; }

    /// <summary>Size (px) of the active tool.</summary>
    public int ToolSize
    {
        get => Settings.SizeFor(ToolSizeTarget);
        set
        {
            var v = Math.Clamp(value, 1, 100);
            if (v == ToolSize)
            {
                return;
            }

            Settings.SetSizeFor(ToolSizeTarget, v);
            OnPropertyChanged();
            (_activeTool as ShapeTool)?.RefreshFromSettings();
            OverlayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when a selection tool is active.</summary>
    public bool IsSelectionToolActive => ActiveToolKind is ToolKind.RectSelect or ToolKind.FreeSelect;

    /// <summary>The shape being drawn when the shape tool is active (null otherwise).</summary>
    public ShapeKind? ActiveShape
    {
        get
        {
            ShapeKind? active = ActiveToolKind == ToolKind.Shape ? ShapeKind : null;
            foreach (var item in ShapeItems)
            {
                item.IsActive = item.Kind == active;
            }

            return active;
        }
    }

    /// <summary>Selects the last-used selection mode (rectangle or free-form).</summary>
    [RelayCommand]
    private void SelectSelectionTool() => ActiveToolKind = _lastSelectMode;

    private ToolKind _lastSelectMode = ToolKind.RectSelect;

    /// <summary>True when the active tool has an adjustable size.</summary>
    public bool ToolHasSize => ToolSettings.HasSize(ActiveToolKind);

    /// <summary>True when the contextual Text toolbar is shown.</summary>
    public bool IsTextToolbarVisible => Text.Session is not null || ActiveToolKind == ToolKind.Text;

    /// <summary>Available font families.</summary>
    public static IReadOnlyList<string> FontFamilies { get; } = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Font sizes offered in the dropdown.</summary>
    public static IReadOnlyList<double> FontSizes { get; } = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>The selection tool (rect or free-form) currently active, if any.</summary>
    internal SelectionTool? ActiveSelection => _activeTool as SelectionTool;

    private ToolKind ToolSizeTarget => ActiveToolKind is ToolKind.Pencil or ToolKind.Eraser or ToolKind.Shape ? ActiveToolKind : ToolKind.Brush;

    /// <summary>Selects a tool.</summary>
    [RelayCommand]
    private void SelectTool(ToolKind kind) => ActiveToolKind = kind;

    /// <summary>Selects a brush (and the brush tool).</summary>
    [RelayCommand]
    private void SelectBrush(BrushKind kind)
    {
        BrushKind = kind;
        ActiveToolKind = ToolKind.Brush;
    }

    /// <summary>Selects a shape (and the shape tool).</summary>
    [RelayCommand]
    private void SelectShape(ShapeKind kind)
    {
        if (_activeTool is ShapeTool { HasPendingOperation: true } st)
        {
            st.CommitPending();
        }

        ShapeKind = kind;
        ActiveToolKind = ToolKind.Shape;
    }

    /// <summary>Sets the outline style.</summary>
    [RelayCommand]
    private void SetOutline(ShapeStyle style) => OutlineStyle = style;

    /// <summary>Sets the fill style.</summary>
    [RelayCommand]
    private void SetFill(ShapeStyle style) => FillStyle = style;

    /// <summary>Swaps color 1 and color 2 (X).</summary>
    [RelayCommand]
    private void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

    /// <summary>Makes color 1 or 2 the target of palette clicks.</summary>
    [RelayCommand]
    private void SetActiveSlot(string? slot) => ActiveColorSlot = slot == "2" ? 2 : 1;

    /// <summary>Palette left click: sets the active color slot (or the text color while editing).</summary>
    [RelayCommand]
    private void PickPaletteColor(Swatch? swatch)
    {
        if (swatch?.Color is not { } c)
        {
            return;
        }

        if (ActiveColorSlot == 1)
        {
            PrimaryColor = c;
        }
        else
        {
            SecondaryColor = c;
        }
    }

    /// <summary>Palette right click: sets color 2.</summary>
    [RelayCommand]
    private void PickPaletteSecondary(Swatch? swatch)
    {
        if (swatch?.Color is { } c)
        {
            SecondaryColor = c;
        }
    }

    /// <summary>Opens the Edit colors dialog for the active slot.</summary>
    [RelayCommand]
    private void EditColors()
    {
        var initial = ActiveColorSlot == 1 ? PrimaryColor : SecondaryColor;
        var result = _dialogs.EditColors(initial, AddCustomColor);
        if (result is { } c)
        {
            if (ActiveColorSlot == 1)
            {
                PrimaryColor = c;
            }
            else
            {
                SecondaryColor = c;
            }
        }
    }

    /// <summary>Adds a color to the first free custom slot (or shifts the oldest out).</summary>
    public void AddCustomColor(Color c)
    {
        var free = CustomColors.FirstOrDefault(s => s.Color is null);
        if (free is null)
        {
            for (var i = 0; i < CustomColors.Count - 1; i++)
            {
                CustomColors[i].Color = CustomColors[i + 1].Color;
            }

            free = CustomColors[^1];
        }

        free.Color = c;
        SaveSettings();
    }

    /// <summary>Changes the active tool's size by a delta (Ctrl+Plus/Minus).</summary>
    public void AdjustSize(int delta) => ToolSize += delta;

    /// <summary>Text: removes the object being edited (Delete text button).</summary>
    [RelayCommand]
    private void DeleteText() => Text.Delete();

    /// <summary>Text: opaque background.</summary>
    [RelayCommand]
    private void MakeTextOpaque() => TextOpaque = true;

    /// <summary>Text: transparent background.</summary>
    [RelayCommand]
    private void MakeTextTransparent() => TextOpaque = false;

    /// <summary>Text: toggles bold (Ctrl+B).</summary>
    [RelayCommand]
    private void ToggleBold() => TextBold = !TextBold;

    /// <summary>Text: toggles italic (Ctrl+I).</summary>
    [RelayCommand]
    private void ToggleItalic() => TextItalic = !TextItalic;

    /// <summary>Text: toggles underline (Ctrl+U).</summary>
    [RelayCommand]
    private void ToggleUnderline() => TextUnderline = !TextUnderline;

    partial void OnActiveToolKindChanging(ToolKind value)
    {
        if (value == ActiveToolKind || _tools.Count == 0)
        {
            return;
        }

        if (value != ToolKind.Text)
        {
            Text.Commit();
        }

        _activeTool.Deactivate();
        if (ActiveToolKind is not ToolKind.Picker)
        {
            _previousTool = ActiveToolKind;
        }
    }

    partial void OnActiveToolKindChanged(ToolKind value)
    {
        if (_tools.Count == 0)
        {
            return;
        }

        _activeTool = _tools[value];
        _activeTool.Activate();
        if (value is ToolKind.RectSelect or ToolKind.FreeSelect)
        {
            _lastSelectMode = value;
        }

        OnPropertyChanged(nameof(IsSelectionToolActive));
        OnPropertyChanged(nameof(ActiveShape));
        OnPropertyChanged(nameof(ToolSize));
        OnPropertyChanged(nameof(ToolHasSize));
        OnPropertyChanged(nameof(IsTextToolbarVisible));
        ToolStateChanged();
    }

    partial void OnPrimaryColorChanged(Color value)
    {
        Settings.Primary = value;
        if (Text.Session is { } s && ActiveColorSlot == 1)
        {
            s.Update(t => t.Foreground = value);
        }

        (_activeTool as ShapeTool)?.RefreshFromSettings();
    }

    partial void OnSecondaryColorChanged(Color value)
    {
        Settings.Secondary = value;
        if (Text.Session is { } s && ActiveColorSlot == 2)
        {
            s.Update(t => t.Background = value);
        }

        (_activeTool as ShapeTool)?.RefreshFromSettings();
        (_activeTool as SelectionTool)?.RefreshTransparency();
    }

    partial void OnBrushKindChanged(BrushKind value) => Settings.Brush = value;

    partial void OnShapeKindChanged(ShapeKind value)
    {
        Settings.Shape = value;
        OnPropertyChanged(nameof(ActiveShape));
    }

    partial void OnOutlineStyleChanged(ShapeStyle value)
    {
        Settings.Outline = value;
        (_activeTool as ShapeTool)?.RefreshFromSettings();
    }

    partial void OnFillStyleChanged(ShapeStyle value)
    {
        Settings.Fill = value;
        (_activeTool as ShapeTool)?.RefreshFromSettings();
    }

    partial void OnOpacityPercentChanged(double value)
    {
        Settings.Opacity = Math.Clamp(value, 1, 100) / 100.0;
        (_activeTool as ShapeTool)?.RefreshFromSettings();
    }

    partial void OnTransparentSelectionChanged(bool value)
    {
        Settings.TransparentSelection = value;
        (_activeTool as SelectionTool)?.RefreshTransparency();
    }

    partial void OnTextFontFamilyChanged(string value) => ApplyText(t => t.FontFamily = value, () => Settings.FontFamily = value);

    partial void OnTextFontSizeChanged(double value)
    {
        var v = Math.Clamp(value, 1, 999);
        ApplyText(t => t.FontSizePt = v, () => Settings.FontSizePt = v);
    }

    partial void OnTextBoldChanged(bool value) => ApplyText(t => t.Bold = value, () => Settings.Bold = value);

    partial void OnTextItalicChanged(bool value) => ApplyText(t => t.Italic = value, () => Settings.Italic = value);

    partial void OnTextUnderlineChanged(bool value) => ApplyText(t => t.Underline = value, () => Settings.Underline = value);

    partial void OnTextStrikethroughChanged(bool value) => ApplyText(t => t.Strikethrough = value, () => Settings.Strikethrough = value);

    partial void OnTextOpaqueChanged(bool value) => ApplyText(t => t.OpaqueBackground = value, () => Settings.TextOpaque = value);

    private void ApplyText(Action<Core.Document.TextObject> change, Action setDefault)
    {
        setDefault();
        if (_loadingTextProps)
        {
            return;
        }

        Text.Session?.Update(change);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTextSessionChanged(object? sender, EventArgs e)
    {
        if (Text.Session is { } s)
        {
            _loadingTextProps = true;
            TextFontFamily = s.Text.FontFamily;
            TextFontSize = s.Text.FontSizePt;
            TextBold = s.Text.Bold;
            TextItalic = s.Text.Italic;
            TextUnderline = s.Text.Underline;
            TextStrikethrough = s.Text.Strikethrough;
            TextOpaque = s.Text.OpaqueBackground;
            _loadingTextProps = false;
            StatusNotice = Strings.Status_EditingText;
            CaretRequest = _textCaretRequest;
            _textCaretRequest = null;
        }
        else if (StatusNotice == Strings.Status_EditingText)
        {
            StatusNotice = null;
        }

        OnPropertyChanged(nameof(IsTextToolbarVisible));
        Layers.Refresh();
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Canvas point where the caret should be placed when the editor opens.</summary>
    public Point? CaretRequest { get; private set; }

    private void LoadSettings(AppSettings s)
    {
        var tool = Enum.TryParse<ToolKind>(s.Tool, out var t) ? t : ToolKind.Pencil;
        ActiveToolKind = tool is ToolKind.Picker or ToolKind.Magnifier ? ToolKind.Pencil : tool;
        BrushKind = Enum.TryParse<BrushKind>(s.Brush, out var b) ? b : BrushKind.Brush;
        ShapeKind = Enum.TryParse<ShapeKind>(s.Shape, out var sh) ? sh : ShapeKind.Rectangle;
        OutlineStyle = Enum.TryParse<ShapeStyle>(s.Outline, out var o) ? o : ShapeStyle.Solid;
        FillStyle = Enum.TryParse<ShapeStyle>(s.Fill, out var f) ? f : ShapeStyle.None;
        Settings.PencilSize = Math.Clamp(s.PencilSize, 1, 100);
        Settings.BrushSize = Math.Clamp(s.BrushSize, 1, 100);
        Settings.EraserSize = Math.Clamp(s.EraserSize, 1, 100);
        Settings.ShapeSize = Math.Clamp(s.ShapeSize, 1, 100);
        OpacityPercent = Math.Clamp(s.Opacity * 100, 1, 100);
        PrimaryColor = ColorUtil.TryParseHex(s.Primary, out var p) ? p : Colors.Black;
        SecondaryColor = ColorUtil.TryParseHex(s.Secondary, out var sc) ? sc : Colors.White;
        TransparentSelection = s.TransparentSelection;
        TextFontFamily = string.IsNullOrWhiteSpace(s.FontFamily) ? "Segoe UI" : s.FontFamily;
        TextFontSize = Math.Clamp(s.FontSizePt, 1, 999);
        CustomColors.Clear();
        for (var i = 0; i < 10; i++)
        {
            Color? c = i < s.CustomColors.Count && ColorUtil.TryParseHex(s.CustomColors[i], out var cc) ? cc : null;
            CustomColors.Add(new Swatch(c, Strings.Color_Custom));
        }

        ShowRulers = s.ShowRulers;
        ShowGridlines = s.ShowGridlines;
        ShowStatusBar = s.ShowStatusBar;
        ShowLayers = s.ShowLayers;
        Theme = s.Theme;
        RecentFiles = [.. s.RecentFiles.Take(10)];
    }

    /// <summary>Writes the current preferences to the settings file.</summary>
    public void SaveSettings()
    {
        var s = _settingsService.Current;
        s.Tool = (ActiveToolKind is ToolKind.Picker or ToolKind.Magnifier ? _previousTool : ActiveToolKind).ToString();
        s.Brush = BrushKind.ToString();
        s.Shape = ShapeKind.ToString();
        s.Outline = OutlineStyle.ToString();
        s.Fill = FillStyle.ToString();
        s.PencilSize = Settings.PencilSize;
        s.BrushSize = Settings.BrushSize;
        s.EraserSize = Settings.EraserSize;
        s.ShapeSize = Settings.ShapeSize;
        s.Opacity = Settings.Opacity;
        s.Primary = PrimaryColor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        s.Secondary = SecondaryColor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        s.TransparentSelection = TransparentSelection;
        s.FontFamily = TextFontFamily;
        s.FontSizePt = TextFontSize;
        s.CustomColors = CustomColors.Select(c => c.Color?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToList();
        s.ShowRulers = ShowRulers;
        s.ShowGridlines = ShowGridlines;
        s.ShowStatusBar = ShowStatusBar;
        s.ShowLayers = ShowLayers;
        s.Theme = Theme;
        s.RecentFiles = [.. RecentFiles];
        _settingsService.Save();
    }

    private static Color C(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
