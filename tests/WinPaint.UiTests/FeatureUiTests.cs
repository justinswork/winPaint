using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.UiTests;

/// <summary>Feature-level UI tests with screenshot evidence (dialogs, layers, selection, view, files, robustness).</summary>
public class FeatureUiTests
{
    private static string TempPng(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winPaintUiTests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}.png");
    }

    [Fact]
    public void Dialogs_ResizeSkew_ImageProperties_Settings_PageSetup_Preview()
    {
        using var s = AppSession.Launch();
        s.Run("drag 100 100 600 400");

        // Resize and skew: 50 % then 20° skew.
        var seq = s.Seq;
        s.RunNoWait("key Ctrl+W");
        s.WaitForWindow("ResizeSkewDialog");
        s.SetText("ResizeHorizontal", "50");
        Assert.Equal("50", s.Find("ResizeVertical").Patterns.Value.Pattern.Value.Value);
        s.SetText("SkewHorizontal", "20");
        s.SnapshotWindow("ResizeSkewDialog", "F-IMG-02_resize_skew_dialog");
        s.Invoke("OkButton");
        s.WaitIdle(seq + 2);
        Assert.Equal(324, s.StateInt("h"));
        Assert.True(s.StateInt("w") > 576);
        s.Snapshot("F-IMG-02_resized_skewed");

        // Image properties: change the canvas size in pixels, check centimeters view.
        seq = s.Seq;
        s.RunNoWait("key Ctrl+E");
        s.WaitForWindow("ImagePropertiesDialog");
        s.Invoke("UnitCm");
        s.SnapshotWindow("ImagePropertiesDialog", "F-FILE-11_image_properties_cm");
        s.Invoke("UnitPixels");
        s.SetText("PropWidth", "800");
        s.SetText("PropHeight", "500");
        s.Invoke("OkButton");
        s.WaitIdle(seq + 2);
        Assert.Equal(800, s.StateInt("w"));
        Assert.Equal(500, s.StateInt("h"));

        // Black and white conversion (with confirmation).
        seq = s.Seq;
        s.RunNoWait("key Ctrl+E");
        s.WaitForWindow("ImagePropertiesDialog");
        s.Invoke("ColorsBW");
        s.Invoke("OkButton");
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton0");
        s.WaitIdle(seq + 1);
        s.Snapshot("F-FILE-11_black_and_white");

        // Settings (theme + about).
        s.Invoke("SettingsButton");
        s.WaitForWindow("SettingsDialog");
        Assert.Contains("Version", s.Find("AboutVersion").Name, StringComparison.Ordinal);
        Assert.Contains(".NET 10", s.Find("AboutDotNet").Name, StringComparison.Ordinal);
        s.SnapshotWindow("SettingsDialog", "F-UI-08_settings_about");
        s.Invoke("OkButton");

        // Page setup + print preview.
        s.ExpandMenu("FileMenu");
        s.ExpandMenu("FilePrintMenu");
        s.Invoke("FilePageSetup");
        s.WaitForWindow("PageSetupDialog");
        s.Invoke("Landscape");
        s.Invoke("FitTo");
        s.SnapshotWindow("PageSetupDialog", "F-FILE-09_page_setup");
        s.Invoke("OkButton");
        s.ExpandMenu("FileMenu");
        s.ExpandMenu("FilePrintMenu");
        s.Invoke("FilePrintPreview");
        s.WaitForWindow("PrintPreviewWindow");
        Assert.Equal("Page 1 of 1", s.Find("PageLabel").Name);
        s.SnapshotWindow("PrintPreviewWindow", "F-FILE-09_print_preview");
        s.Find("PrintPreviewWindow").Patterns.Window.Pattern.Close();
    }

    [Fact]
    public void Layers_PanelOperations()
    {
        using var s = AppSession.Launch();
        s.Invoke("LayersToggle");
        Assert.Equal("1", s.State("layersPanel"));
        s.Invoke("Shape_Rectangle");
        s.Invoke("FillButton");
        s.InvokeByName("Solid color");
        s.Invoke("Color2Button");
        s.Run("palette 7");
        s.Invoke("Color1Button");
        s.Run("drag 100 100 500 400; key Enter");
        s.Invoke("LayerAdd");
        Assert.Equal(2, s.StateInt("layers"));
        Assert.Equal(1, s.StateInt("activeLayer"));
        s.Invoke("ToolBrush");
        s.Run("palette 3; drag 50 250 700 260");
        s.Invoke("ToolText");
        s.Run("palette 0; click 150 150");
        s.TypeInEditor("On layer 2");
        s.Run("click 900 600");
        s.Invoke("LayerDuplicate");
        Assert.Equal(3, s.StateInt("layers"));
        Assert.Equal(2, s.StateInt("texts"));
        s.Snapshot("F-LAY-01_layers_panel");
        s.Invoke("LayerDelete");
        Assert.Equal(2, s.StateInt("layers"));
        s.Run("key Ctrl+Z");
        Assert.Equal(3, s.StateInt("layers"));
        s.Invoke("LayerMoveDown");
        s.Invoke("LayerMoveUp");
        s.Invoke("LayerMergeDown");
        Assert.Equal(2, s.StateInt("layers"));
        Assert.Equal(2, s.StateInt("texts"));

        // Flatten (asks for confirmation; text becomes pixels).
        s.Invoke("LayerFlatten");
        s.WaitForWindow("MessageDialog");
        s.SnapshotWindow("MessageDialog", "F-LAY-06_flatten_confirm");
        s.Invoke("DialogButton0");
        AppSession.WaitUntil(() => s.StateInt("layers") == 1 && s.StateInt("texts") == 0, TimeSpan.FromSeconds(10));
        s.Snapshot("F-LAY-06_flattened");
    }

    [Fact]
    public void Selection_FreeForm_ContextMenu_Transparent()
    {
        using var s = AppSession.Launch();
        s.Invoke("Shape_Oval");
        s.Invoke("FillButton");
        s.InvokeByName("Solid color");
        s.Invoke("Color2Button");
        s.Run("palette 16");
        s.Invoke("Color1Button");
        s.Run("drag 100 100 400 300; key Enter");

        // Free-form selection, then move it.
        s.Invoke("SelectionDropDown");
        s.Invoke("SelFreeForm");
        Assert.Equal("FreeSelect", s.State("tool"));
        s.Run("drag 120 120 380 110 390 290 110 280 120 120");
        Assert.Equal("1", s.State("selectionActive"));
        s.Snapshot("F-SEL-02_free_form_selected");
        s.Run("drag 250 200 600 300");
        Assert.Equal("1", s.State("floating"));
        s.Snapshot("F-SEL-05_free_form_moved");
        s.Run("key Enter");

        // Context menu on a rectangular selection: Invert colors.
        s.Invoke("SelectionDropDown");
        s.Invoke("SelRectangle");
        s.Run("drag 550 250 900 450");
        s.Run("down 700 350 right; up 700 350 right");
        s.InvokeByName("Invert colors");
        Assert.Equal("1", s.State("floating"));
        s.Snapshot("F-SEL-05_context_menu_invert");
        s.Run("key Escape");

        // Transparent selection toggle.
        s.Invoke("SelectionDropDown");
        s.Invoke("SelTransparent");
        s.Run("drag 80 80 420 320; drag 200 200 300 420");
        s.Snapshot("F-SEL-04_transparent_selection");
        s.Run("key Enter");
    }

    [Fact]
    public void View_CompactToolbar_Rulers_Thumbnail_CanvasResize_TransparentCanvas()
    {
        using var s = AppSession.Launch();
        s.Run("drag 10 10 1100 600");

        // Narrow window: groups collapse into dropdowns.
        s.Run("resizewindow 720 700");
        Thread.Sleep(300);
        s.Snapshot("F-UI-05_compact_toolbar");
        Assert.NotNull(s.MainWindow.FindFirstDescendant(c => c.ByName("Colors").And(c.ByControlType(FlaUI.Core.Definitions.ControlType.Button))));
        s.Run("resizewindow 1280 820");
        Thread.Sleep(300);
        Assert.Null(s.MainWindow.FindFirstDescendant(c => c.ByName("Colors").And(c.ByControlType(FlaUI.Core.Definitions.ControlType.Button))));

        // Rulers + status bar + thumbnail.
        s.Run("key Ctrl+R; hover 300 200");
        s.Snapshot("F-VIEW-03_rulers");
        s.ExpandMenu("ViewMenu");
        s.Invoke("ViewThumbnail");
        s.WaitForWindow("ThumbnailWindow");
        s.SnapshotWindow("ThumbnailWindow", "F-VIEW-07_thumbnail");
        s.Find("ThumbnailWindow").Patterns.Window.Pattern.Close();

        // Canvas resize by dragging the right and bottom-right handles.
        s.Run("key Ctrl+1; scrolltocanvas 900 400");
        s.Run("drag 1158 324 1300 324");
        Assert.Equal(1300, s.StateInt("w"));
        s.Run("drag 1306 654 1400 700");
        Assert.Equal(1400, s.StateInt("w"));
        Assert.Equal(700, s.StateInt("h"));
        s.Run("key Ctrl+0");
        s.Snapshot("F-IMG-03_canvas_resized");

        // Transparent canvas (checkerboard shows through).
        s.ExpandMenu("EditMenu");
        s.Invoke("EditTransparentCanvas");
        Assert.Equal("1", s.State("transparentCanvas"));
        s.Snapshot("F-IMG-05_transparent_canvas");
    }

    [Fact]
    public void Files_IcoSave_RecentFiles_WallpaperPrompt()
    {
        var ico = Path.ChangeExtension(TempPng("icon"), ".ico");
        using (var s = AppSession.Launch())
        {
            s.Invoke("Shape_Heart");
            s.Run("drag 100 50 500 450; key Enter");

            // Set as desktop background on an unsaved image asks to save first (Cancel keeps the wallpaper unchanged).
            var seq = s.Seq;
            s.ExpandMenu("FileMenu");
            s.InvokeByName("Set as desktop background");
            s.InvokeByName("Fill");
            s.WaitForWindow("MessageDialog");
            s.SnapshotWindow("MessageDialog", "F-FILE-10_wallpaper_save_first");
            s.Invoke("DialogButton1");

            // Save as ICO: size picker + non-square padding warning.
            seq = s.Seq;
            s.RunNoWait("key F12");
            var dlg = s.WaitForNativeDialog();
            AppSession.FileDialogAccept(dlg, ico);
            s.WaitForWindow("IcoSizesDialog");
            s.SnapshotWindow("IcoSizesDialog", "F-FILE-05_ico_sizes");
            s.Invoke("Ico64");
            s.Invoke("OkButton");
            s.WaitForWindow("MessageDialog");
            s.Invoke("DialogButton0");
            s.WaitIdle(seq + 1);
            AppSession.WaitUntil(() => File.Exists(ico), TimeSpan.FromSeconds(10));
            s.ExpandMenu("FileMenu");
            s.ExpandMenu("FileRecent");
            Assert.NotNull(s.FindNow("FileRecent")!.FindFirstDescendant(c => c.ByName("_1 " + Path.GetFileName(ico)).Or(c.ByName("1 " + Path.GetFileName(ico)))));
            s.Snapshot("F-FILE-07_recent_files");
        }

    }

    [Fact]
    public void Robustness_OpenErrors_CrashHandler_LargeImage()
    {
        // Corrupt file from the command line: friendly error, the app keeps running.
        var bad = TempPng("corrupt");
        File.WriteAllBytes(bad, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
        using (var s = AppSession.Launch(bad))
        {
            s.WaitForWindow("MessageDialog");
            s.SnapshotWindow("MessageDialog", "F-ROB-02_open_error");
            s.Invoke("DialogButton0");
            Assert.False(s.HasExited);
            Assert.Equal(0, s.StateInt("undo"));
        }

        // Unhandled exception: logged, recovery copy offered and saved.
        var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winPaint", "logs");
        var logCount = Directory.Exists(logs) ? Directory.GetFiles(logs).Length : 0;
        using (var s = AppSession.Launch())
        {
            s.Run("drag 10 10 300 300");
            s.RunNoWait("crashtest");
            s.WaitForWindow("MessageDialog");
            s.SnapshotWindow("MessageDialog", "F-ROB-01_crash_dialog");
            s.Invoke("DialogButton0");
            s.WaitForWindow("MessageDialog");
            s.Invoke("DialogButton0");
            AppSession.WaitUntil(() => s.HasExited, TimeSpan.FromSeconds(15));
            Assert.True(Directory.GetFiles(s.RecoveryDir, "*.png").Length == 1);
        }

        Assert.True(Directory.GetFiles(logs).Length > logCount);

        // 10000 × 10000 image opens and works.
        var big = TempPng("big");
        var img = new PixelBuffer(10000, 10000, ColorUtil.White);
        img.Fill(new PixelRect(4000, 4000, 2000, 2000), ColorUtil.Pack(255, 0, 120, 255));
        ImageCodec.Encode(img, big, ImageFormat.Png);
        using (var s = AppSession.Launch(big))
        {
            AppSession.WaitUntil(() => s.StateInt("w") == 10000, TimeSpan.FromSeconds(60), "large image did not open");
            s.Invoke("ToolBrush");
            s.Run("drag 100 100 9900 9900");
            Assert.Equal(1, s.StateInt("undo"));
            s.Snapshot("F-ROB-03_large_image");
            s.Run("key Ctrl+Shift+I");
            AppSession.WaitUntil(() => s.StateInt("undo") == 2 && s.State("busy") == "0", TimeSpan.FromSeconds(60), "invert of the large image did not finish");
        }
    }

    [Fact]
    public void TextToolbar_FormattingAndDelete()
    {
        using var s = AppSession.Launch();
        s.Invoke("ToolText");
        s.Run("click 100 100");
        s.TypeInEditor("Formatting");
        s.SetText("FontFamilyBox", "Georgia");
        s.SetText("FontSizeBox", "28");
        s.Invoke("ItalicButton");
        s.Invoke("StrikeButton");
        s.Invoke("TextOpaqueButton");
        s.Invoke("Color2Button");
        s.Run("palette 15");
        AppSession.WaitUntil(() => s.State("editSize") == "28", TimeSpan.FromSeconds(10));
        Assert.Equal("True", s.State("editItalic"));
        Assert.Equal("True", s.State("editOpaque"));
        s.Snapshot("F-TOOL-04_text_toolbar_formatting");
        s.Invoke("DeleteTextButton");
        Assert.Equal("0", s.State("editing"));
        Assert.Equal(0, s.StateInt("texts"));
        Assert.Equal(0, s.StateInt("undo"));
    }

    [Fact]
    public void SizeOpacityAndEditColorsCustom()
    {
        using var s = AppSession.Launch();
        s.Invoke("ToolBrush");
        s.Invoke("SizeButton");
        s.SetText("SizeBox", "40");
        Assert.Equal(40, s.StateInt("size"));
        s.SetText("OpacityBox", "40");
        s.SnapshotWindow("MainWindow", "F-SIZE-01_size_popup");
        s.Invoke("SizeButton");
        s.Run("palette 3; drag 100 200 900 200; palette 8; drag 500 50 500 500");
        var comp = ImageCheck.Load(s.Composite(TempPng("opacity")));
        var mid = comp[500, 200];
        Assert.True(((mid >> 16) & 0xFF) < 230 && ((mid >> 16) & 0xFF) > 60, "40 % strokes blend");
        s.Snapshot("F-SIZE-02_opacity_strokes");

        s.Invoke("EditColorsButton");
        s.WaitForWindow("EditColorsDialog");
        s.SetText("HexBox", "#12AB34");
        s.Invoke("AddCustomButton");
        s.Invoke("OkButton");
        AppSession.WaitUntil(() => s.State("primary") == "#FF12AB34", TimeSpan.FromSeconds(10));
        Assert.Equal("#FF12AB34", s.State("custom0"));
        s.Snapshot("F-COL-02_custom_color_added");
    }
}
