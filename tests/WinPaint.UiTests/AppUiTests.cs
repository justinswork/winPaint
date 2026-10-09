using System.Globalization;

namespace WinPaint.UiTests;

/// <summary>UI tests U-06..U-08 (themes, unsaved prompt, keyboard shortcuts).</summary>
public class AppUiTests
{
    private static void SampleDrawing(AppSession s)
    {
        s.Invoke("Shape_Heart");
        s.Invoke("FillButton");
        s.InvokeByName("Solid color");
        s.Run("palette 3; drag 100 100 300 280; key Enter");
        s.Invoke("ToolBrush");
        s.Run("palette 8; drag 400 120 600 200 800 140");
        s.Invoke("ToolText");
        s.Run("palette 0; click 420 300");
        s.TypeInEditor("winPaint");
        s.Run("click 900 600");
    }

    /// <summary>U-06: dark and light mode screenshots of the main window.</summary>
    [Fact]
    public void U06_DarkAndLight()
    {
        using (var s = AppSession.Launch(theme: "Dark"))
        {
            Assert.Equal("Dark", s.State("theme"));
            SampleDrawing(s);
            s.Invoke("LayersToggle");
            s.Snapshot("U-06_dark");
        }

        using (var s = AppSession.Launch(theme: "Light"))
        {
            Assert.Equal("Light", s.State("theme"));
            SampleDrawing(s);
            s.Invoke("LayersToggle");
            s.Snapshot("U-06_light");

            // Switch at runtime through View ▸ Theme; the image itself never changes with the theme.
            var before = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "u06a.png")));
            s.ExpandMenu("ViewMenu");
            s.ExpandMenu("ViewTheme");
            s.Invoke("ThemeDark");
            AppSession.WaitUntil(() => s.State("theme") == "Dark", TimeSpan.FromSeconds(10));
            var after = ImageCheck.Load(s.Composite(Path.Combine(Path.GetTempPath(), "u06b.png")));
            Assert.Equal(0, ImageCheck.Diff(before, after, 0, 0, before.Width, before.Height));
            s.Snapshot("U-06_switched_to_dark");
        }
    }

    /// <summary>U-07: the unsaved-changes prompt appears on close after an edit.</summary>
    [Fact]
    public void U07_UnsavedPromptOnClose()
    {
        using var s = AppSession.Launch();
        s.Run("drag 50 50 400 300");
        Assert.Equal("1", s.State("dirty"));
        Assert.StartsWith("*", s.State("title"), StringComparison.Ordinal);
        s.CloseMainWindow();
        s.WaitForWindow("MessageDialog");
        s.SnapshotWindow("MessageDialog", "U-07_unsaved_prompt");

        // Cancel keeps the app open.
        s.Invoke("DialogButton2");
        Thread.Sleep(500);
        Assert.False(s.HasExited);

        // Don't save closes it.
        s.CloseMainWindow();
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton1");
        AppSession.WaitUntil(() => s.HasExited, TimeSpan.FromSeconds(15), "app did not exit");
    }

    /// <summary>U-08: every row of the §5.12 shortcut table triggers its command.</summary>
    [Fact]
    public void U08_KeyboardShortcuts()
    {
        using var s = AppSession.Launch();
        double Zoom() => double.Parse(s.State("zoom")!, CultureInfo.InvariantCulture);
        void Dialog(string gesture, string windowId, string cancelId = "CancelButton")
        {
            var seq = s.Seq;
            s.RunNoWait($"key {gesture}");
            s.WaitForWindow(windowId);
            s.Invoke(cancelId);
            s.WaitIdle(seq + 1);
        }

        void Native(string gesture)
        {
            var seq = s.Seq;
            s.RunNoWait($"key {gesture}");
            var dlg = s.WaitForNativeDialog();
            AppSession.NativeCancel(dlg);
            s.WaitIdle(seq + 1);
        }

        // Ctrl+N (with the unsaved prompt), Ctrl+O, Ctrl+S, F12, Ctrl+Shift+S, Ctrl+P, Ctrl+E.
        s.Run("drag 10 10 100 100");
        var seqN = s.Seq;
        s.RunNoWait("key Ctrl+N");
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton1");
        s.WaitIdle(seqN + 1);
        Assert.Equal(0, s.StateInt("undo"));
        Native("Ctrl+O");
        Native("Ctrl+S");
        Native("F12");
        Native("Ctrl+Shift+S");
        var seqP = s.Seq;
        s.RunNoWait("key Ctrl+P");
        s.CancelPrintDialog();
        s.WaitIdle(seqP + 1);
        Dialog("Ctrl+E", "ImagePropertiesDialog");
        Dialog("Ctrl+W", "ResizeSkewDialog");

        // Undo / redo.
        s.Run("drag 10 10 100 100");
        Assert.Equal(1, s.StateInt("undo"));
        s.Run("key Ctrl+Z");
        Assert.Equal(0, s.StateInt("undo"));
        s.Run("key Ctrl+Y");
        Assert.Equal(1, s.StateInt("undo"));
        s.Run("key Ctrl+Z; key Ctrl+Shift+Z");
        Assert.Equal(1, s.StateInt("undo"));

        // Select all, copy, cut, paste, delete, escape.
        s.Run("key Ctrl+A");
        Assert.Equal("1", s.State("selectionActive"));
        s.Run("key Ctrl+C");
        s.Run("key Ctrl+X");
        Assert.Equal(2, s.StateInt("undo"));
        s.Run("key Ctrl+V");
        Assert.Equal("1", s.State("floating"));
        s.Run("key Escape");
        Assert.Equal("0", s.State("floating"));
        Assert.Equal(3, s.StateInt("undo"));
        s.Run("key Ctrl+A; key Delete");
        Assert.Equal(4, s.StateInt("undo"));

        // Arrows nudge the selection (1 px, Shift = 10 px).
        s.Run("drag 100 100 200 200");
        s.Run("key Right; key Shift+Down");
        Assert.Equal("1", s.State("floating"));
        Assert.StartsWith("PixelRect { X = 101, Y = 110", s.State("selBounds"), StringComparison.Ordinal);
        s.Run("key Enter");

        // Crop and invert.
        s.Run("drag 0 0 500 400; key Ctrl+Shift+X");
        Assert.Equal(500, s.StateInt("w"));
        var undo = s.StateInt("undo");
        s.Run("key Ctrl+Shift+I");
        Assert.Equal(undo + 1, s.StateInt("undo"));

        // Rulers, gridlines.
        s.Run("key Ctrl+R; key Ctrl+G");
        Assert.Equal("1", s.State("rulers"));
        Assert.Equal("1", s.State("grid"));
        s.Snapshot("U-08_rulers_grid");

        // Zoom: Ctrl+PgUp/PgDn, Ctrl+wheel, Ctrl+0, Ctrl+1.
        s.Run("key Ctrl+1");
        Assert.Equal(1, Zoom());
        s.Run("key Ctrl+PageUp");
        Assert.Equal(2, Zoom());
        s.Run("key Ctrl+PageDown");
        Assert.Equal(1, Zoom());
        s.Run("wheel 100 100 120 ctrl");
        Assert.Equal(2, Zoom());
        s.Run("key Ctrl+0");
        Assert.True(Zoom() <= 1);

        // Ctrl+Plus/Minus: size for brush tools, zoom otherwise.
        s.Invoke("ToolBrush");
        var size = s.StateInt("size");
        s.Run("key Ctrl+Plus");
        Assert.Equal(size + 1, s.StateInt("size"));
        s.Run("key Ctrl+Minus");
        Assert.Equal(size, s.StateInt("size"));
        s.Invoke("ToolFill");
        var z0 = Zoom();
        s.Run("key Ctrl+Plus");
        Assert.True(Zoom() > z0);

        // X swaps colors.
        var p = s.State("primary");
        var sec = s.State("secondary");
        s.Run("key X");
        Assert.Equal(sec, s.State("primary"));
        Assert.Equal(p, s.State("secondary"));
        s.Run("key X");

        // Text editing: Ctrl+B/I/U, Enter (multi-line), Delete with the box selected.
        s.Invoke("ToolText");
        s.Run("click 50 50");
        s.TypeInEditor("Line one\nLine two");
        s.Run("key Ctrl+B; key Ctrl+I; key Ctrl+U");
        Assert.Equal("True", s.State("editBold"));
        Assert.Equal("True", s.State("editItalic"));
        Assert.Equal("True", s.State("editUnderline"));
        Assert.Contains("\\n", s.State("editText"), StringComparison.Ordinal);
        s.Run("key Escape");
        Assert.Equal("0", s.State("editing"));
        var texts = s.StateInt("texts");
        s.Run("dblclick 60 55; focuscanvas; key Delete");
        Assert.Equal(texts - 1, s.StateInt("texts"));

        // F11 full screen, F1 shortcuts help.
        var seqF = s.Seq;
        s.RunNoWait("key F11");
        var full = s.WaitForWindow("FullScreenWindow");
        s.SnapshotWindow("FullScreenWindow", "U-08_full_screen");
        full.Patterns.Window.Pattern.Close();
        s.WaitIdle(seqF + 1);
        var seqH = s.Seq;
        s.RunNoWait("key F1");
        s.WaitForWindow("ShortcutsDialog");
        s.SnapshotWindow("ShortcutsDialog", "U-08_shortcuts_dialog");
        s.Invoke("OkButton");
        s.WaitIdle(seqH + 2);

        // Tab / Shift+Tab: toolbar controls are keyboard focusable.
        foreach (var id in new[] { "ToolPencil", "ToolBrush", "Shape_Oval", "SizeButton", "Color1Button", "EditColorsButton", "LayersToggle" })
        {
            Assert.True(s.Find(id).Properties.IsKeyboardFocusable.Value, id);
        }
    }
}
