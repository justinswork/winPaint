using System.Globalization;

namespace WinPaint.UiTests;

/// <summary>UI tests for the live-text feature: U-05, T-WYSIWYG and the Definition-of-Done walk-through.</summary>
public class TextUiTests
{
    internal static string SaveAs(AppSession s, string path)
    {
        var seq = s.Seq;
        s.RunNoWait("key F12");
        var dlg = s.WaitForNativeDialog();
        AppSession.FileDialogAccept(dlg, path);
        s.WaitIdle(seq + 1);
        AppSession.WaitUntil(() => s.State("file") == path, TimeSpan.FromSeconds(20), "file was not saved");
        return path;
    }

    private static (int X, int Y, int W, int H) Rect(string? value)
    {
        var p = (value ?? throw new InvalidOperationException("no edit rect")).Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return (p[0], p[1], p[2], p[3]);
    }

    /// <summary>U-05: full end-to-end run of the text feature.</summary>
    [Fact]
    public void U05_TextEndToEnd()
    {
        var file = Path.Combine(Path.GetTempPath(), "winPaintUiTests", $"u05-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (var s = AppSession.Launch())
        {
            s.Invoke("ToolText");
            s.Run("click 200 150");
            Assert.Equal("1", s.State("editing"));
            s.TypeInEditor("Hello from the UI test");
            s.Snapshot("U-05_1_typing");
            s.Run("click 900 500");
            Assert.Equal("0", s.State("editing"));
            Assert.Equal(1, s.StateInt("texts"));
            Assert.Equal(1, s.StateInt("undo"));

            s.Invoke("ToolBrush");
            s.Run("palette 3; drag 150 160 400 175 700 150");
            var before = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "u05a.png")));
            s.Snapshot("U-05_2_painted_over");

            s.Invoke("ToolSelect");
            s.Run("dblclick 230 160");
            Assert.Equal("1", s.State("editing"));
            Assert.Equal("Hello from the UI test", s.State("editText"));
            s.TypeInEditor("Edited after painting");
            s.Snapshot("U-05_3_reopened_edited");
            s.Run("click 900 500");
            Assert.Equal(1, s.StateInt("texts"));
            var after = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "u05b.png")));
            var redBefore = 0;
            for (var y = 0; y < before.Height; y++)
            {
                for (var x = 0; x < before.Width; x++)
                {
                    var p = before[x, y];
                    if (((p >> 16) & 0xFF) == 0xED && ((p >> 8) & 0xFF) == 0x1C && (p & 0xFF) == 0x24)
                    {
                        redBefore++;
                        Assert.Equal(p, after[x, y]);
                    }
                }
            }

            Assert.True(redBefore > 300, "the red stroke must be found");
            s.Snapshot("U-05_4_committed");
            SaveAs(s, file);
            Assert.Equal("0", s.State("dirty"));
            Assert.Equal(1, s.StateInt("texts"));
        }

        using (var s = AppSession.Launch(file))
        {
            // The project is embedded in the PNG (docs/specs/wpp-format.md), so the text is editable again.
            AppSession.WaitUntil(() => s.State("file") == file, TimeSpan.FromSeconds(20));
            Assert.Equal(1, s.StateInt("texts"));
            Assert.Equal(0, s.StateInt("undo"));
            Assert.Equal("0", s.State("dirty"));
            s.Invoke("ToolSelect");
            s.Run("dblclick 230 160");
            Assert.Equal("1", s.State("editing"));
            s.Snapshot("U-05_5_reopened_editable");
        }
    }

    /// <summary>T-WYSIWYG: edit mode vs committed mode at 50/100/300/800 % — no glyph shift &gt; 1 screen px.</summary>
    [Fact]
    public void TWysiwyg()
    {
        using var s = AppSession.Launch();
        s.Run("palette 0");
        s.Invoke("ToolText");
        s.Run("click 60 60");
        s.TypeInEditor("WYSIWYG check: caret & glyphs line up, wrapping too.\nSecond line");
        s.Run("click 900 500");
        foreach (var zoom in new[] { 0.5, 1, 3, 8 })
        {
            var z = zoom.ToString(CultureInfo.InvariantCulture);
            // The Text tool keeps the Text toolbar visible in both modes, so the canvas does not move between snapshots.
            s.Invoke("ToolText");
            s.Run($"zoom {z}; scrollto 0 0; scrolltocanvas 50 50");
            s.Run("dblclick 70 70");
            Assert.Equal("1", s.State("editing"));
            s.Run("caret 0; measurecaret");
            var dev = double.Parse(s.State("caretDev")!, CultureInfo.InvariantCulture);
            Assert.True(dev <= 1, $"caret/glyph deviation {dev} px at zoom {z}");
            var rect = Rect(s.State("editRect"));
            var view = Rect(s.State("viewRect"));
            var edit = ImageCheck.Load(s.Snapshot($"T-WYSIWYG_{z}_edit"));
            s.Run("key Escape");
            Assert.Equal("0", s.State("editing"));
            var committed = ImageCheck.Load(s.Snapshot($"T-WYSIWYG_{z}_committed"));

            // Inside the box (away from the dashed frame and the caret at index 0) both renderings must be identical.
            var inset = 6;
            var x = rect.X + inset;
            var y = rect.Y + inset;
            var w = Math.Min(rect.W - (2 * inset), view.X + view.W - x);
            var h = Math.Min(rect.H - (2 * inset), view.Y + view.H - y);
            var diff = ImageCheck.Diff(edit, committed, x, y, w, h);
            Assert.True(diff <= h * 2, $"zoom {z}: {diff} pixels differ inside the text box");
            Assert.True(committed.Ink(x, y, w, h) > 50, $"zoom {z}: no glyphs visible");
        }
    }

    /// <summary>Definition of Done §12.6: the scripted manual walk-through.</summary>
    [Fact]
    public void DoD_WalkThrough()
    {
        var file = Path.Combine(Path.GetTempPath(), "winPaintUiTests", $"dod-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (var s = AppSession.Launch())
        {
            // New image.
            s.Run("key Ctrl+N");
            s.Snapshot("DoD_01_new_image");

            // Blue rectangle (filled).
            s.Invoke("Shape_Rectangle");
            s.Invoke("FillButton");
            s.InvokeByName("Solid color");
            s.Run("palette 8");
            s.Invoke("Color2Button");
            s.Run("palette 8");
            s.Invoke("Color1Button");
            s.Run("drag 100 80 600 380; key Enter");
            s.Snapshot("DoD_02_blue_rectangle");

            // Text "Draft v1".
            s.Run("palette 0");
            s.Invoke("ToolText");
            s.Run("click 150 150");
            s.TypeInEditor("Draft v1");
            s.Run("click 1000 600");
            Assert.Equal(1, s.StateInt("texts"));
            s.Snapshot("DoD_03_text_draft_v1");

            // Red scribble across the text.
            s.Invoke("ToolBrush");
            s.Run("palette 3; drag 120 170 200 140 260 190 330 145 400 185");
            var scribble = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "dod-a.png")));
            s.Snapshot("DoD_04_red_scribble");

            // Select tool, double-click the text, edit to "Final v2", bold, 36 pt, wider box.
            s.Invoke("ToolSelect");
            s.Run("dblclick 165 160");
            Assert.Equal("1", s.State("editing"));
            s.TypeInEditor("Final v2");
            s.Invoke("BoldButton");
            s.SetText("FontSizeBox", "36");
            s.Run("focuscanvas");
            AppSession.WaitUntil(() => s.State("editSize") == "36", TimeSpan.FromSeconds(10), "font size not applied");
            Assert.Equal("True", s.State("editBold"));
            var box = s.State("editFrame")!.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var right = box[0] + box[2];
            var midY = box[1] + (box[3] / 2);
            s.Run($"drag {right.ToString(CultureInfo.InvariantCulture)} {midY.ToString(CultureInfo.InvariantCulture)} {(right + 200).ToString(CultureInfo.InvariantCulture)} {midY.ToString(CultureInfo.InvariantCulture)}");
            var widened = s.State("editBox")!.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            Assert.True(widened[2] > box[2] + 150, "box should be wider");
            s.Snapshot("DoD_05_editing_final_v2");

            // Click outside; the red scribble is still on top and unchanged.
            s.Run("click 1000 600");
            Assert.Equal("0", s.State("editing"));
            var edited = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "dod-b.png")));
            var red = 0;
            for (var y = 0; y < scribble.Height; y++)
            {
                for (var x = 0; x < scribble.Width; x++)
                {
                    var p = scribble[x, y];
                    if (((p >> 16) & 0xFF) == 0xED && ((p >> 8) & 0xFF) == 0x1C && (p & 0xFF) == 0x24)
                    {
                        red++;
                        Assert.Equal(p, edited[x, y]);
                    }
                }
            }

            Assert.True(red > 300);
            s.Snapshot("DoD_06_committed_scribble_on_top");

            // Rotate 90° right, double-click the text and change its color.
            s.Invoke("RotateButton");
            s.Invoke("RotateRight");
            Assert.Equal(648, s.StateInt("w"));
            s.Snapshot("DoD_07_rotated");
            s.Run("dblclick 470 190");
            Assert.Equal("1", s.State("editing"));
            Assert.Equal("Final v2", s.State("editText"));
            s.Run("palette 6");
            s.Snapshot("DoD_08_rotated_editing_color");
            s.Run("click 30 1100");
            Assert.Equal("0", s.State("editing"));

            // Save as PNG.
            SaveAs(s, file);
            s.Snapshot("DoD_09_saved");
        }

        // Close, reopen the PNG: the embedded project brings the text back, still editable.
        using (var s = AppSession.Launch(file))
        {
            AppSession.WaitUntil(() => s.State("file") == file, TimeSpan.FromSeconds(20));
            Assert.Equal(1, s.StateInt("texts"));
            s.Invoke("ToolSelect");
            s.Run("dblclick 470 190");
            Assert.Equal("1", s.State("editing"));
            Assert.Equal("Final v2", s.State("editText"));
            s.Snapshot("DoD_10_reopened_editable");
        }
    }
}
