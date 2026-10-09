using WinPaint.Core.Brushes;
using WinPaint.Core.Shapes;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WinPaint.UiTests;

/// <summary>UI tests U-01..U-04 (drawing, shapes, colors, zoom).</summary>
public class DrawingUiTests
{
    /// <summary>U-01: launch, draw with each brush, screenshot.</summary>
    [Fact]
    public void U01_DrawWithEachBrush()
    {
        using var s = AppSession.Launch();
        s.Run("palette 8");
        var y = 40;
        foreach (var kind in Enum.GetValues<BrushKind>())
        {
            s.Invoke("BrushDropDown");
            s.Invoke($"Brush_{kind}");
            Assert.Equal("Brush", s.State("tool"));
            Assert.StartsWith(kind.ToString(), s.State("tool2"), StringComparison.Ordinal);
            s.Run($"drag 60 {y} 300 {y + 20} 560 {y - 10} 800 {y + 10}");
            if (kind == BrushKind.Airbrush)
            {
                s.Run($"down 900 {y}; tick; tick; tick; tick; up 900 {y}");
            }

            y += 64;
        }

        var shot = s.Snapshot("U-01_brushes");
        var comp = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "u01.png")));
        y = 40;
        foreach (var kind in Enum.GetValues<BrushKind>())
        {
            Assert.True(comp.Ink(60, y - 25, 750, 60) > 200, $"{kind} stroke missing");
            y += 64;
        }

        Assert.True(s.StateInt("undo") >= 9);
        Assert.True(File.Exists(shot));
    }

    /// <summary>U-02: every shape in the gallery can be selected and drawn.</summary>
    [Fact]
    public void U02_EachShape()
    {
        using var s = AppSession.Launch();
        var i = 0;
        foreach (var kind in Enum.GetValues<ShapeKind>())
        {
            var col = i % 6;
            var row = i / 6;
            int x = 30 + (col * 185), y = 25 + (row * 155);
            s.Invoke($"Shape_{kind}");
            Assert.Equal("Shape", s.State("tool"));
            switch (kind)
            {
                case ShapeKind.Curve:
                    s.Run($"drag {x} {y + 120} {x + 150} {y + 120}; drag {x + 40} {y} {x + 40} {y + 5}; drag {x + 110} {y + 140} {x + 110} {y + 140}");
                    break;
                case ShapeKind.Polygon:
                    s.Run($"drag {x} {y + 120} {x + 70} {y}; click {x + 150} {y + 120}; click {x + 75} {y + 90}; dblclick {x + 75} {y + 90}");
                    break;
                default:
                    s.Run($"drag {x} {y} {x + 150} {y + 120}");
                    break;
            }

            Assert.Equal(1, s.StateInt("floating"));
            s.Run("key Enter");
            Assert.Equal(0, s.StateInt("floating"));
            i++;
        }

        s.Snapshot("U-02_shapes");
        var comp = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "u02.png")));
        i = 0;
        foreach (var kind in Enum.GetValues<ShapeKind>())
        {
            var col = i % 6;
            var row = i / 6;
            Assert.True(comp.Ink(30 + (col * 185) - 3, 25 + (row * 155) - 3, 158, 128) > 100, $"{kind} not drawn");
            i++;
        }

        Assert.Equal(23, s.StateInt("undo"));
    }

    [Fact]
    public void U02b_ShapeStylesAndAdjust()
    {
        using var s = AppSession.Launch();
        s.Invoke("Shape_FivePointStar");
        s.Run("drag 100 100 300 280");
        s.Invoke("FillButton");
        s.InvokeByName("Solid color");
        s.Invoke("Color2Button");
        s.Run("palette 14");
        s.Snapshot("U-02_shape_floating_with_handles");
        Assert.Equal(1, s.StateInt("floating"));
        s.Run("drag 200 190 450 300");
        Assert.Equal(0, s.StateInt("undo"));
        s.Run("click 20 600");
        Assert.Equal(1, s.StateInt("undo"));
    }

    /// <summary>U-03: Edit colors dialog, enter hex #3366CC, Color 1 updates.</summary>
    [Fact]
    public void U03_EditColorsHex()
    {
        using var s = AppSession.Launch();
        s.Invoke("EditColorsButton");
        s.WaitForWindow("EditColorsDialog");
        s.SetText("HexBox", "#3366CC");
        Assert.Equal("51", s.Find("RedBox").Patterns.Value.Pattern.Value.Value);
        s.SnapshotWindow("EditColorsDialog", "U-03_edit_colors_dialog");
        s.Invoke("OkButton");
        AppSession.WaitUntil(() => s.State("primary") == "#FF3366CC", TimeSpan.FromSeconds(10), "Color 1 did not update");
        s.Snapshot("U-03_color1_updated");
    }

    /// <summary>U-04: zoom to 800 % with gridlines on.</summary>
    [Fact]
    public void U04_Zoom800WithGridlines()
    {
        using var s = AppSession.Launch();
        s.Invoke("ToolPencil");
        s.Run("palette 3; drag 10 10 40 25 20 40; palette 7; drag 15 30 45 12");
        s.Run("key Ctrl+G");
        Assert.Equal("1", s.State("grid"));
        for (var i = 0; i < 12; i++)
        {
            s.Run("key Ctrl+PageUp");
        }

        Assert.Equal("8", s.State("zoom"));
        s.Run("scrollto 0 0");
        s.Snapshot("U-04_zoom800_gridlines");
        s.Run("key Ctrl+1");
        Assert.Equal("1", s.State("zoom"));
        s.Run("key Ctrl+0");
        Assert.NotEqual("8", s.State("zoom"));
    }
}
