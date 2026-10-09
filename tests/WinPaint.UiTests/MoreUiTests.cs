using System.Diagnostics;
using System.Globalization;
using FlaUI.Core.Definitions;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.UiTests;

/// <summary>Further UI coverage: persistence, tooltips/names, open/import/exit flows, panning, leaks, start-up time.</summary>
public class MoreUiTests
{
    private static string WriteImage(string name, int w, int h, uint color, bool transparent = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winPaintUiTests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{name}-{Guid.NewGuid():N}.png");
        var img = new PixelBuffer(w, h, transparent ? 0u : ColorUtil.White);
        img.Fill(new PixelRect(w / 4, h / 4, w / 2, h / 2), color);
        ImageCodec.Encode(img, path, ImageFormat.Png);
        return path;
    }

    /// <summary>F-UI-06: settings persist across sessions.</summary>
    [Fact]
    public void SettingsPersistAcrossSessions()
    {
        string settings;
        using (var s = AppSession.Launch(theme: "Dark"))
        {
            s.KeepSettings = true;
            settings = s.SettingsPath;
            s.Invoke("ToolBrush");
            s.Run("key Ctrl+Plus; key Ctrl+Plus; palette 6; key Ctrl+R; key Ctrl+G");
            s.Invoke("LayersToggle");
            s.Invoke("EditColorsButton");
            s.WaitForWindow("EditColorsDialog");
            s.SetText("HexBox", "#102030");
            s.Invoke("AddCustomButton");
            s.Invoke("OkButton");
            s.Run("resizewindow 1100 760");
            s.CloseMainWindow();
            AppSession.WaitUntil(() => s.HasExited, TimeSpan.FromSeconds(15));
        }

        using (var s = AppSession.Launch(reuseSettings: settings))
        {
            Assert.Equal("Dark", s.State("theme"));
            Assert.Equal("Brush", s.State("tool"));
            Assert.Equal(10, s.StateInt("size"));
            Assert.Equal("#FF102030", s.State("primary"));
            Assert.Equal("#FF102030", s.State("custom0"));
            Assert.Equal("1", s.State("rulers"));
            Assert.Equal("1", s.State("grid"));
            Assert.Equal("1", s.State("layersPanel"));
            Assert.InRange(s.MainWindow.BoundingRectangle.Width, 1050, 1150);
        }

        Directory.Delete(Path.GetDirectoryName(settings)!, true);
    }

    /// <summary>F-UI-04 / F-UI-07: every toolbar control has a tooltip and an accessible name.</summary>
    [Fact]
    public void ToolbarTooltipsAndNames()
    {
        using var s = AppSession.Launch();
        var toolbar = s.Find("Toolbar");
        var buttons = toolbar.FindAllDescendants(c => c.ByControlType(ControlType.Button)).ToList();
        Assert.True(buttons.Count > 60, $"only {buttons.Count} toolbar buttons found");
        foreach (var b in buttons)
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Name), $"button {b.AutomationId} has no name");
            Assert.False(string.IsNullOrWhiteSpace(b.HelpText), $"button {b.AutomationId} ({b.Name}) has no tooltip");
        }

        Assert.Contains("Ctrl+W", s.Find("ResizeButton").HelpText, StringComparison.Ordinal);
        Assert.Contains("X", s.Find("SwapColorsButton").HelpText, StringComparison.Ordinal);
    }

    /// <summary>F-FILE-02/06/08/12/13: open (dialog + drop), prompts, import, recent missing file, exit.</summary>
    [Fact]
    public void OpenImportDropExitFlows()
    {
        var red = WriteImage("red", 400, 300, ColorUtil.Pack(255, 237, 28, 36));
        var big = WriteImage("big", 2000, 1500, ColorUtil.Pack(255, 0, 162, 232));
        using var s = AppSession.Launch();

        // Open through the dialog.
        var seq = s.Seq;
        s.RunNoWait("key Ctrl+O");
        AppSession.FileDialogAccept(s.WaitForNativeDialog(), red);
        s.WaitIdle(seq + 1);
        AppSession.WaitUntil(() => s.State("file") == red, TimeSpan.FromSeconds(15));
        Assert.Equal(400, s.StateInt("w"));

        // Edit → Open again asks to save; Cancel keeps the document.
        s.Run("drag 10 10 200 200");
        seq = s.Seq;
        s.RunNoWait("key Ctrl+O");
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton2");
        s.WaitIdle(seq + 1);
        Assert.Equal("1", s.State("dirty"));

        // Import (paste from file) larger than the canvas offers to enlarge it.
        s.ExpandMenu("FileMenu");
        s.InvokeByName("Import to canvas");
        s.InvokeByName("From a file...");
        AppSession.FileDialogAccept(s.WaitForNativeDialog(), big);
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton0");
        AppSession.WaitUntil(() => s.State("floating") == "1", TimeSpan.FromSeconds(15));
        Assert.Equal(2000, s.StateInt("w"));
        s.Snapshot("F-FILE-08_imported_enlarged");
        s.Run("key Enter");

        // Drop a file: prompt (dirty) → Don't save → opened.
        s.RunNoWait($"drop {red}");
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton1");
        AppSession.WaitUntil(() => s.State("file") == red && s.StateInt("w") == 400, TimeSpan.FromSeconds(15));

        // Recent file that no longer exists is removed with a message.
        File.Delete(big);
        var gone = WriteImage("gone", 50, 50, ColorUtil.Black);
        s.RunNoWait($"drop {gone}");
        AppSession.WaitUntil(() => s.State("file") == gone, TimeSpan.FromSeconds(15));
        File.Delete(gone);
        s.ExpandMenu("FileMenu");
        s.ExpandMenu("FileRecent");
        s.InvokeByName("_1 " + Path.GetFileName(gone));
        s.WaitForWindow("MessageDialog");
        s.SnapshotWindow("MessageDialog", "F-FILE-07_recent_missing");
        s.Invoke("DialogButton0");

        // Exit from the menu with unsaved changes: prompt → Don't save.
        s.Run("drag 10 10 200 200");
        s.ExpandMenu("FileMenu");
        s.InvokeByName("Exit");
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton1");
        AppSession.WaitUntil(() => s.HasExited, TimeSpan.FromSeconds(15), "Exit did not close the app");
    }

    /// <summary>F-FILE-05: JPEG with transparency warns before the first lossy save; GIF warns about colors.</summary>
    [Fact]
    public void FormatWarnings()
    {
        var src = WriteImage("transparent", 200, 150, ColorUtil.Pack(255, 0, 0, 255), transparent: true);
        using var s = AppSession.Launch(src);
        AppSession.WaitUntil(() => s.State("file") == src, TimeSpan.FromSeconds(15));
        Assert.Equal("1", s.State("transparentCanvas"));
        var jpg = Path.ChangeExtension(src, ".jpg");
        var seq = s.Seq;
        s.RunNoWait("key F12");
        AppSession.FileDialogAccept(s.WaitForNativeDialog(), jpg);
        s.WaitForWindow("MessageDialog");
        s.SnapshotWindow("MessageDialog", "F-FILE-05_jpeg_transparency_warning");
        s.Invoke("DialogButton0");
        s.WaitIdle(seq + 1);
        AppSession.WaitUntil(() => File.Exists(jpg), TimeSpan.FromSeconds(10));
        var gif = Path.ChangeExtension(src, ".gif");
        seq = s.Seq;
        s.RunNoWait("key F12");
        AppSession.FileDialogAccept(s.WaitForNativeDialog(), gif);
        s.WaitForWindow("MessageDialog");
        s.Invoke("DialogButton0");
        s.WaitIdle(seq + 1);
        AppSession.WaitUntil(() => File.Exists(gif), TimeSpan.FromSeconds(10));
        var bmp = Path.ChangeExtension(src, ".bmp");
        seq = s.Seq;
        s.RunNoWait("key F12");
        AppSession.FileDialogAccept(s.WaitForNativeDialog(), bmp);
        s.WaitIdle(seq + 1);
        AppSession.WaitUntil(() => File.Exists(bmp), TimeSpan.FromSeconds(10));
        Assert.Equal(32, BitConverter.ToUInt16(File.ReadAllBytes(bmp), 28));
    }

    /// <summary>F-VIEW-02: wheel, Shift+wheel, middle-button and Space+drag panning; F-VIEW-05 status bar toggle.</summary>
    [Fact]
    public void ScrollAndPan()
    {
        using var s = AppSession.Launch();
        s.Run("zoom 4; scrollto 0 0");
        Assert.Equal("0,0", s.State("scroll"));
        s.Run("wheel 50 50 -240");
        Assert.Equal("0,96", s.State("scroll"));
        s.Run("wheel 50 50 -120 shift");
        Assert.Equal("48,96", s.State("scroll"));
        s.Run("drag 100 100 80 90 middle");
        Assert.Equal("128,136", s.State("scroll"));
        s.Run("drag 100 100 110 100 space");
        Assert.Equal("88,136", s.State("scroll"));
        Assert.Equal(0, s.StateInt("undo"));
        s.ExpandMenu("ViewMenu");
        s.InvokeByName("Status bar");
        Assert.Equal("0", s.State("statusBar"));
        s.Snapshot("F-VIEW-05_status_bar_hidden");
    }

    /// <summary>F-ROB-04 + start-up time: two instances run side by side; cold start is measured.</summary>
    [Fact]
    public void MultiInstanceAndStartupTime()
    {
        var sw = Stopwatch.StartNew();
        using var a = AppSession.Launch();
        var startup = sw.Elapsed;
        using var b = AppSession.Launch();
        Assert.NotEqual(a.ProcessId, b.ProcessId);
        a.Run("drag 10 10 100 100");
        Assert.Equal(1, a.StateInt("undo"));
        Assert.Equal(0, b.StateInt("undo"));
        AppSession.WaitUntil(() => !string.IsNullOrEmpty(a.State("startupMs")), TimeSpan.FromSeconds(10));
        var firstFrame = double.Parse(a.State("startupMs")!, CultureInfo.InvariantCulture);
        File.WriteAllText(
            Path.Combine(AppSession.ScreenshotDir, "startup-time.txt"),
            string.Create(CultureInfo.InvariantCulture, $"Process start to first rendered frame: {firstFrame:0} ms{Environment.NewLine}Launch to UI Automation ready (includes UIA attach): {startup.TotalMilliseconds:0} ms{Environment.NewLine}"));
        Assert.True(firstFrame < 1500, $"cold start {firstFrame} ms");
    }

    /// <summary>§9: 20 open/close-image cycles do not grow memory without bound.</summary>
    [Fact]
    public void NoLeakOverTwentyOpenCycles()
    {
        var img = WriteImage("leak", 1920, 1080, ColorUtil.Pack(255, 30, 160, 60));
        using var s = AppSession.Launch();
        var proc = Process.GetProcessById(s.ProcessId);
        long Sample()
        {
            s.Run("gc");
            proc.Refresh();
            return proc.PrivateMemorySize64;
        }

        for (var i = 0; i < 3; i++)
        {
            s.Run($"drop {img}");
            AppSession.WaitUntil(() => s.State("busy") == "0" && s.State("file") == img, TimeSpan.FromSeconds(15));
            s.Run("key Ctrl+N");
        }

        var baseline = Sample();
        for (var i = 0; i < 20; i++)
        {
            s.Run($"drop {img}");
            AppSession.WaitUntil(() => s.State("busy") == "0" && s.State("file") == img && s.StateInt("w") == 1920, TimeSpan.FromSeconds(15));
            s.Run("drag 10 10 1000 900");
            s.Run("key Ctrl+Z");
            s.Run("key Ctrl+N");
        }

        var after = Sample();
        var growthMb = (after - baseline) / (1024.0 * 1024.0);
        File.WriteAllText(Path.Combine(AppSession.ScreenshotDir, "leak-test.txt"), string.Create(CultureInfo.InvariantCulture, $"Private bytes after warm-up: {baseline / 1048576.0:0} MB; after 20 open/close cycles: {after / 1048576.0:0} MB; growth {growthMb:0.0} MB{Environment.NewLine}"));
        Assert.True(growthMb < 60, $"memory grew by {growthMb:0} MB over 20 cycles");
    }
}
