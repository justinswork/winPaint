using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.Text;
using WinPaint.Core.Tools;

namespace WinPaint.Core.Tests;

/// <summary>Minimal tool host for driving tools in tests.</summary>
internal sealed class FakeHost : IToolHost
{
    private int _seed;

    public FakeHost(PaintDocument doc)
    {
        Document = doc;
        Text = new TextEditController(() => Document, () => new TextObject
        {
            FontFamily = Settings.FontFamily,
            FontSizePt = Settings.FontSizePt,
            Foreground = Settings.Primary,
            Background = Settings.Secondary,
            OpaqueBackground = Settings.TextOpaque,
        });
    }

    public PaintDocument Document { get; set; }

    public ToolSettings Settings { get; } = new();

    public TextEditController Text { get; }

    public string? Notice { get; private set; }

    public int RestoreCount { get; private set; }

    public List<(Point Point, bool In)> Zooms { get; } = [];

    public bool IsEditingText => Text.Session is not null;

    public void ShowNotice(string? text) => Notice = text;

    public void SetColor(bool primary, Color color)
    {
        if (primary)
        {
            Settings.Primary = color;
        }
        else
        {
            Settings.Secondary = color;
        }
    }

    public void RestorePreviousTool() => RestoreCount++;

    public void ZoomStep(Point canvasPoint, bool zoomIn) => Zooms.Add((canvasPoint, zoomIn));

    public int NextSeed() => ++_seed;

    public void ToolStateChanged()
    {
    }

    public bool TryBeginTextEditAt(Point canvasPoint) => Text.TryBeginAt(canvasPoint);

    public void BeginNewText(Rect box) => Text.BeginNew(box);

    public void CommitTextEdit() => Text.Commit();

    public double HandleTolerance => 4;

    public int FlattenNotices { get; private set; }

    public int ContextMenus { get; private set; }

    public void TextFlattened() => FlattenNotices++;

    public void ShowContextMenu() => ContextMenus++;

    /// <summary>Drags a tool along points with the given button.</summary>
    public static void Drag(ITool tool, PointerButton button, params Point[] points) => Drag(tool, button, ModifierKeys.None, points);

    /// <summary>Drags a tool along points with modifiers.</summary>
    public static void Drag(ITool tool, PointerButton button, ModifierKeys mods, params Point[] points)
    {
        tool.OnPointerDown(new PointerInput(points[0], button, mods));
        foreach (var p in points.Skip(1))
        {
            tool.OnPointerMove(new PointerInput(p, button, mods));
        }

        tool.OnPointerUp(new PointerInput(points[^1], button, mods));
    }

    /// <summary>Simulates a double click (two down/up pairs, the second with ClickCount 2).</summary>
    public static void DoubleClick(ITool tool, Point p, PointerButton button = PointerButton.Left)
    {
        tool.OnPointerDown(new PointerInput(p, button, ModifierKeys.None, 1));
        tool.OnPointerUp(new PointerInput(p, button, ModifierKeys.None, 1));
        tool.OnPointerDown(new PointerInput(p, button, ModifierKeys.None, 2));
        tool.OnPointerUp(new PointerInput(p, button, ModifierKeys.None, 2));
    }
}
