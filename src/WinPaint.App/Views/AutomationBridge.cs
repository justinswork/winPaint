using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPaint.App.ViewModels;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.App.Views;

/// <summary>
/// Non-pointer commands of the canvas automation interface: keyboard gestures routed through the same shortcut
/// table as real keys, window snapshots rendered in-process (works on locked or remote desktops), and a state
/// summary for automation clients.
/// </summary>
internal static class AutomationBridge
{
    /// <summary>Result of the last "measurecaret" command (screen px).</summary>
    public static double LastCaretDeviation { get; private set; } = double.NaN;

    /// <summary>State summary as "key=value" pairs separated by '|'.</summary>
    public static string State(MainViewModel vm, MainWindow window)
    {
        var doc = vm.Document;
        var t = vm.Text.Session?.Text;
        var pairs = new (string, object?)[]
        {
            ("title", vm.Title),
            ("tool", vm.ActiveToolKind),
            ("zoom", vm.Zoom.ToString("0.###", CultureInfo.InvariantCulture)),
            ("w", doc.Width),
            ("h", doc.Height),
            ("undo", doc.History.UndoCount),
            ("redo", doc.History.RedoCount),
            ("texts", doc.AllText.Count()),
            ("editing", t is null ? 0 : 1),
            ("editText", t?.Text.Replace("|", "/", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)),
            ("editBox", t is null ? null : string.Create(CultureInfo.InvariantCulture, $"{t.Box.X},{t.Box.Y},{t.Box.Width},{t.Box.Height}")),
            ("editBold", t?.Bold),
            ("editItalic", t?.Italic),
            ("editUnderline", t?.Underline),
            ("editOpaque", t?.OpaqueBackground),
            ("editColor", t?.Foreground.ToString(CultureInfo.InvariantCulture)),
            ("selBounds", vm.ActiveSelection?.HasSelection == true ? vm.ActiveSelection.SelectionBounds.ToString() : string.Empty),
            ("transparentCanvas", vm.IsTransparentCanvas ? 1 : 0),
            ("statusBar", vm.ShowStatusBar ? 1 : 0),
            ("layersPanel", vm.ShowLayers ? 1 : 0),
            ("editSize", t?.FontSizePt.ToString(CultureInfo.InvariantCulture)),
            ("dirty", doc.IsDirty ? 1 : 0),
            ("notice", vm.StatusNotice),
            ("selection", vm.SelectionText),
            ("layers", doc.Layers.Count),
            ("activeLayer", doc.ActiveLayerIndex),
            ("primary", vm.PrimaryColor.ToString(CultureInfo.InvariantCulture)),
            ("secondary", vm.SecondaryColor.ToString(CultureInfo.InvariantCulture)),
            ("size", vm.ToolSize),
            ("rulers", vm.ShowRulers ? 1 : 0),
            ("grid", vm.ShowGridlines ? 1 : 0),
            ("theme", vm.Theme),
            ("file", vm.FilePath),
            ("editRect", EditRect(window)),
            ("viewRect", ViewRect(window)),
            ("editFrame", t is null ? null : FrameText(Core.Text.TextLayoutEngine.EffectiveBox(t))),
            ("caretDev", LastCaretDeviation.ToString("0.###", CultureInfo.InvariantCulture)),
            ("caretDiag", window.Canvas.Editor.Diag),
            ("tool2", vm.BrushKind + "/" + vm.ShapeKind),
            ("selectionActive", vm.HasSelection ? 1 : 0),
            ("floating", vm.Document.Floating is null ? 0 : 1),
            ("cursor", vm.CursorText),
            ("imageSize", vm.ImageSizeText),
            ("windows", string.Join(",", Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != window).Select(w => System.Windows.Automation.AutomationProperties.GetAutomationId(w)))),
        };
        return string.Join('|', pairs.Select(p => $"{p.Item1}={Convert.ToString(p.Item2, CultureInfo.InvariantCulture)}"));
    }

    /// <summary>Executes a command; returns false when unknown.</summary>
    public static bool Execute(string command, MainWindow window, MainViewModel vm)
    {
        var space = command.IndexOf(' ', StringComparison.Ordinal);
        var verb = (space < 0 ? command : command[..space]).ToLowerInvariant();
        var arg = space < 0 ? string.Empty : command[(space + 1)..].Trim();
        switch (verb)
        {
            case "key":
                var (key, mods) = ParseGesture(arg);
                window.HandleShortcut(key, mods);
                return true;
            case "snapshot":
                SaveSnapshot(window.RootElement, arg);
                return true;
            case "snapshotwindow":
                var sp = arg.IndexOf(' ', StringComparison.Ordinal);
                var id = arg[..sp];
                var target = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => System.Windows.Automation.AutomationProperties.GetAutomationId(w) == id);
                if (target?.Content is FrameworkElement fe)
                {
                    SaveSnapshot(fe, arg[(sp + 1)..].Trim());
                }

                return true;
            case "composite":
                ImageCodec.Encode(vm.Document.Flatten(includeFloating: true), arg, ImageFormat.Png);
                return true;
            case "focuscanvas":
                window.Canvas.Viewport.Focus();
                return true;
            case "measurecaret":
                LastCaretDeviation = window.Canvas.Editor.MaxCaretDeviation();
                return true;
            case "selectalltext":
                window.Canvas.Editor.SelectAll();
                return true;
            case "caret":
                window.Canvas.Editor.TextBox.CaretIndex = int.Parse(arg, CultureInfo.InvariantCulture);
                return true;
            case "palette":
                vm.PickPaletteColorCommand.Execute(vm.Palette[int.Parse(arg, CultureInfo.InvariantCulture)]);
                return true;
            case "resizewindow":
                var wh = arg.Split(' ');
                window.WindowState = WindowState.Normal;
                window.Width = double.Parse(wh[0], CultureInfo.InvariantCulture);
                window.Height = double.Parse(wh[1], CultureInfo.InvariantCulture);
                window.UpdateLayout();
                return true;
            case "zoom":
                vm.Zoom = double.Parse(arg, CultureInfo.InvariantCulture);
                return true;
            case "scrolltocanvas":
                var cxy = arg.Split(' ');
                var viewPt = window.Canvas.Transform.CanvasToView(new Point(double.Parse(cxy[0], CultureInfo.InvariantCulture), double.Parse(cxy[1], CultureInfo.InvariantCulture)));
                window.Canvas.ScrollBy(viewPt.X, viewPt.Y);
                return true;
            case "scrollto":
                var xy = arg.Split(' ');
                window.Canvas.ScrollBy(double.Parse(xy[0], CultureInfo.InvariantCulture) - window.Canvas.Transform.ScrollX, double.Parse(xy[1], CultureInfo.InvariantCulture) - window.Canvas.Transform.ScrollY);
                return true;
        }

        return false;
    }

    private static string ViewRect(MainWindow window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var vp = window.Canvas.Viewport;
        var tl = vp.TranslatePoint(new Point(0, 0), window.RootElement);
        return string.Create(CultureInfo.InvariantCulture, $"{tl.X * dpi.DpiScaleX:0},{tl.Y * dpi.DpiScaleY:0},{vp.ActualWidth * dpi.DpiScaleX:0},{vp.ActualHeight * dpi.DpiScaleY:0}");
    }

    private static string FrameText(Rect r) => string.Create(CultureInfo.InvariantCulture, $"{r.X:0.##},{r.Y:0.##},{r.Width:0.##},{r.Height:0.##}");

    /// <summary>The edited text box in snapshot pixel coordinates ("x,y,w,h"), or empty.</summary>
    private static string EditRect(MainWindow window)
    {
        var poly = window.Canvas.Editor.FrameViewPolygon();
        if (poly.Count == 0)
        {
            return string.Empty;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var pts = poly.Select(p => window.Canvas.Viewport.TranslatePoint(p, window.RootElement)).ToList();
        var x = pts.Min(p => p.X) * dpi.DpiScaleX;
        var y = pts.Min(p => p.Y) * dpi.DpiScaleY;
        var r = pts.Max(p => p.X) * dpi.DpiScaleX;
        var b = pts.Max(p => p.Y) * dpi.DpiScaleY;
        return string.Create(CultureInfo.InvariantCulture, $"{x:0},{y:0},{r - x:0},{b - y:0}");
    }

    /// <summary>Parses "Ctrl+Shift+S" style gestures.</summary>
    public static (Key Key, ModifierKeys Mods) ParseGesture(string gesture)
    {
        var mods = ModifierKeys.None;
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var m in parts.Take(parts.Length - 1))
        {
            mods |= m.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "shift" => ModifierKeys.Shift,
                "alt" => ModifierKeys.Alt,
                _ => ModifierKeys.None,
            };
        }

        var k = parts[^1];
        var key = k.ToLowerInvariant() switch
        {
            "plus" => Key.OemPlus,
            "minus" => Key.OemMinus,
            "pgup" or "pageup" => Key.PageUp,
            "pgdn" or "pagedown" => Key.PageDown,
            "del" => Key.Delete,
            "esc" => Key.Escape,
            "0" => Key.D0,
            "1" => Key.D1,
            _ => Enum.Parse<Key>(k, ignoreCase: true),
        };
        return (key, mods);
    }

    /// <summary>Renders an element to a PNG at the monitor's DPI.</summary>
    public static void SaveSnapshot(FrameworkElement element, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var w = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
        {
            var brush = (Brush?)element.TryFindResource("ChromeBackgroundBrush") ?? Brushes.White;
            dc.DrawRectangle(brush, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        rtb.Render(bg);
        rtb.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
