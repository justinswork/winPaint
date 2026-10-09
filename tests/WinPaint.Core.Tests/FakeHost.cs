using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.Tools;

namespace WinPaint.Core.Tests;

/// <summary>Minimal tool host for driving tools in tests.</summary>
internal sealed class FakeHost(PaintDocument doc) : IToolHost
{
    private int _seed;

    public PaintDocument Document { get; set; } = doc;

    public ToolSettings Settings { get; } = new();

    public string? Notice { get; private set; }

    public int RestoreCount { get; private set; }

    public List<(Point Point, bool In)> Zooms { get; } = [];

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
}
