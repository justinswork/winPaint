using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace WinPaint.UiTests;

/// <summary>
/// A running winPaint instance driven through UI Automation (FlaUI/UIA3). Toolbar and dialog controls are used through
/// their UIA patterns; drawing goes through the canvas's automation Value pattern (pointer scripts in canvas pixels),
/// which uses the same input handlers as the mouse. Screenshots are rendered by the app itself so they also work on a
/// locked or remote desktop.
/// </summary>
public sealed class AppSession : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly Application _app;
    private readonly string _settingsPath;

    private AppSession(Application app, string settingsPath)
    {
        _app = app;
        _settingsPath = settingsPath;
        MainWindow = Retry(() => _app.GetMainWindow(_automation, TimeSpan.FromSeconds(5)), TimeSpan.FromSeconds(30))
            ?? throw new InvalidOperationException("Main window did not appear.");
        Canvas = WaitFor(() => MainWindow.FindFirstDescendant(c => c.ByAutomationId("Canvas")), TimeSpan.FromSeconds(15));
        WaitUntil(() => State("seq") is not null, TimeSpan.FromSeconds(10));
    }

    /// <summary>Settings file used by this session.</summary>
    public string SettingsPath => _settingsPath;

    /// <summary>Keeps the settings folder on dispose (to relaunch with the same settings).</summary>
    public bool KeepSettings { get; set; }

    /// <summary>Folder where crash recovery copies are written for this session.</summary>
    public string RecoveryDir => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "recovery");

    /// <summary>The main window.</summary>
    public Window MainWindow { get; }

    /// <summary>The canvas element.</summary>
    public AutomationElement Canvas { get; }

    /// <summary>The process id.</summary>
    public int ProcessId => _app.ProcessId;

    /// <summary>True when the app has exited.</summary>
    public bool HasExited => _app.HasExited;

    /// <summary>Repository root (folder containing winPaint.sln).</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "winPaint.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    /// <summary>Screenshot folder (artifacts/screenshots).</summary>
    public static string ScreenshotDir => Path.Combine(RepoRoot, "artifacts", "screenshots");

    /// <summary>Path of the app executable for the current build configuration.</summary>
    public static string ExePath
    {
        get
        {
#if DEBUG
            const string config = "Debug";
#else
            const string config = "Release";
#endif
            return Path.Combine(RepoRoot, "src", "WinPaint.App", "bin", config, "net10.0-windows", "winPaint.exe");
        }
    }

    /// <summary>Launches winPaint with isolated settings.</summary>
    public static AppSession Launch(string? file = null, string theme = "Light", int width = 1280, int height = 820, Dictionary<string, object>? extraSettings = null, string? reuseSettings = null)
    {
        var settingsPath = reuseSettings ?? Path.Combine(Path.GetTempPath(), "winPaintUiTests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        if (reuseSettings is not null)
        {
            return Start(file, settingsPath);
        }

        var settings = new Dictionary<string, object> { ["Theme"] = theme, ["WindowWidth"] = width, ["WindowHeight"] = height, ["WindowLeft"] = 40, ["WindowTop"] = 40 };
        foreach (var kv in extraSettings ?? [])
        {
            settings[kv.Key] = kv.Value;
        }

        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
        return Start(file, settingsPath);
    }

    private static AppSession Start(string? file, string settingsPath)
    {
        var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false };
        if (file is not null)
        {
            psi.ArgumentList.Add(file);
        }

        psi.Environment["WINPAINT_SETTINGS"] = settingsPath;
        psi.Environment["WINPAINT_RECOVERY_DIR"] = Path.Combine(Path.GetDirectoryName(settingsPath)!, "recovery");
        return new AppSession(Application.Launch(psi), settingsPath);
    }

    /// <summary>Runs a canvas automation script and waits until it has executed.</summary>
    public void Run(string script)
    {
        var seq = int.Parse(State("seq")!, System.Globalization.CultureInfo.InvariantCulture);
        Canvas.Patterns.Value.Pattern.SetValue(script);
        WaitUntil(() => int.Parse(State("seq") ?? "0", System.Globalization.CultureInfo.InvariantCulture) > seq, TimeSpan.FromSeconds(30), $"script '{script}' did not complete");
        var error = State("error");
        if (!string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>Queues a script that may open a modal dialog (does not wait).</summary>
    public void RunNoWait(string script) => Canvas.Patterns.Value.Pattern.SetValue(script);

    /// <summary>Waits for the queued scripts to finish (after a modal dialog closed).</summary>
    public void WaitIdle(int expectedSeq) =>
        WaitUntil(() => int.Parse(State("seq") ?? "0", System.Globalization.CultureInfo.InvariantCulture) >= expectedSeq, TimeSpan.FromSeconds(30), "queued script did not finish");

    /// <summary>The current sequence number of completed scripts.</summary>
    public int Seq => int.Parse(State("seq") ?? "0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>All state values.</summary>
    public Dictionary<string, string> States()
    {
        var raw = Canvas.Patterns.Value.Pattern.Value.Value ?? string.Empty;
        return raw.Split('|').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First()[1]);
    }

    /// <summary>One state value.</summary>
    public string? State(string key) => States().GetValueOrDefault(key);

    /// <summary>Integer state value.</summary>
    public int StateInt(string key) => int.Parse(State(key) ?? "0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Finds an element by automation id in the main window or any of the app's windows.</summary>
    public AutomationElement Find(string automationId, double timeoutSeconds = 10) =>
        WaitFor(() => FindNow(automationId), TimeSpan.FromSeconds(timeoutSeconds), $"element '{automationId}' not found");

    /// <summary>Finds an element now or returns null.</summary>
    public AutomationElement? FindNow(string automationId)
    {
        var inMain = MainWindow.FindFirstDescendant(c => c.ByAutomationId(automationId));
        if (inMain is not null)
        {
            return inMain;
        }

        foreach (var w in Windows().Where(w => !w.Equals(MainWindow)))
        {
            var e = w.AutomationId == automationId ? w : w.FindFirstDescendant(c => c.ByAutomationId(automationId));
            if (e is not null)
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>All top-level windows of the app (main window, dialogs, popups).</summary>
    public IReadOnlyList<AutomationElement> Windows()
    {
        var desktop = _automation.GetDesktop();
        var cf = new ConditionFactory(_automation.PropertyLibrary);
        var list = desktop.FindAllChildren(cf.ByProcessId(_app.ProcessId)).ToList();
        foreach (var w in list.ToList())
        {
            list.AddRange(w.FindAllChildren(cf.ByControlType(ControlType.Window)));
        }

        return list;
    }

    /// <summary>Waits for a window with the given automation id.</summary>
    public AutomationElement WaitForWindow(string automationId, double timeoutSeconds = 15) =>
        WaitFor(() => Windows().FirstOrDefault(w => w.AutomationId == automationId), TimeSpan.FromSeconds(timeoutSeconds), $"window '{automationId}' did not appear");

    /// <summary>Waits for a native (Win32 common) dialog of the app.</summary>
    public AutomationElement WaitForNativeDialog(double timeoutSeconds = 15) =>
        WaitFor(
            () => MainWindow.FindFirstChild(c => c.ByClassName("#32770")),
            TimeSpan.FromSeconds(timeoutSeconds),
            "native dialog did not appear");

    /// <summary>Invokes a button/menu item by automation id.</summary>
    public void Invoke(string automationId)
    {
        var e = Find(automationId);
        if (e.Patterns.Invoke.IsSupported)
        {
            e.Patterns.Invoke.Pattern.Invoke();
        }
        else if (e.Patterns.Toggle.IsSupported)
        {
            e.Patterns.Toggle.Pattern.Toggle();
        }
        else if (e.Patterns.SelectionItem.IsSupported)
        {
            e.Patterns.SelectionItem.Pattern.Select();
        }
        else
        {
            throw new InvalidOperationException($"'{automationId}' cannot be invoked.");
        }

        Thread.Sleep(80);
    }

    /// <summary>Invokes the first element (e.g. a menu item) with the given accessible name.</summary>
    public void InvokeByName(string name)
    {
        var e = WaitFor(
            () => Windows()
                .SelectMany(w => w.FindAllDescendants(c => c.ByName(name)))
                .FirstOrDefault(x => x.Patterns.Invoke.IsSupported || x.Patterns.ExpandCollapse.IsSupported || x.Patterns.Toggle.IsSupported || x.Patterns.SelectionItem.IsSupported),
            TimeSpan.FromSeconds(10),
            $"element named '{name}' not found");
        if (e.Patterns.Invoke.IsSupported)
        {
            e.Patterns.Invoke.Pattern.Invoke();
        }
        else if (e.Patterns.ExpandCollapse.IsSupported)
        {
            e.Patterns.ExpandCollapse.Pattern.Expand();
        }
        else if (e.Patterns.SelectionItem.IsSupported)
        {
            e.Patterns.SelectionItem.Pattern.Select();
        }
        else
        {
            e.Patterns.Toggle.Pattern.Toggle();
        }

        Thread.Sleep(120);
    }

    /// <summary>Expands a menu item (File, Edit, …) and returns it.</summary>
    public AutomationElement ExpandMenu(string automationId)
    {
        var e = Find(automationId);
        e.Patterns.ExpandCollapse.Pattern.Expand();
        Thread.Sleep(150);
        return e;
    }

    /// <summary>Sets a text value (TextBox) by automation id.</summary>
    public void SetText(string automationId, string value)
    {
        Find(automationId).Patterns.Value.Pattern.SetValue(value);
        Thread.Sleep(80);
    }

    /// <summary>Types into the open text editor (replaces its content).</summary>
    public void TypeInEditor(string text) => SetText("TextEditor", text);

    /// <summary>Saves an in-app rendered screenshot of the main window to artifacts/screenshots.</summary>
    public string Snapshot(string name)
    {
        var path = Path.Combine(ScreenshotDir, name + ".png");
        Run($"snapshot {path}");
        Assert.True(File.Exists(path), $"screenshot {path} missing");
        return path;
    }

    /// <summary>Saves a screenshot of another app window (by automation id).</summary>
    public string SnapshotWindow(string automationId, string name)
    {
        var path = Path.Combine(ScreenshotDir, name + ".png");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        RunNoWait($"snapshotwindow {automationId} {path}");
        WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0, TimeSpan.FromSeconds(15), $"window snapshot {name} missing");
        Thread.Sleep(200);
        return path;
    }

    /// <summary>Saves the flattened composite (including floating content).</summary>
    public string Composite(string path)
    {
        Run($"composite {path}");
        return path;
    }

    /// <summary>Closes the main window (sends WM_CLOSE).</summary>
    public void CloseMainWindow() => MainWindow.Patterns.Window.Pattern.Close();

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (!_app.HasExited)
            {
                _app.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }

        _app.Dispose();
        _automation.Dispose();
        if (!KeepSettings)
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(_settingsPath)!, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Types a path into a Windows common file dialog and presses its Save/Open button.</summary>
    public static void FileDialogAccept(AutomationElement dialog, string path)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        // Save dialogs use id 1001 for the file name box, Open dialogs 1148.
        var edit = WaitFor(
            () => dialog.FindFirstDescendant(c => c.ByAutomationId("1001").Or(c.ByAutomationId("1148")).And(c.ByControlType(ControlType.Edit))),
            TimeSpan.FromSeconds(15),
            "file name box not found");
        edit.Patterns.Value.Pattern.SetValue(path);
        Thread.Sleep(200);
        var ok = WaitFor(
            () => dialog.FindFirstDescendant(c => c.ByAutomationId("1").And(c.ByControlType(ControlType.Button).Or(c.ByControlType(ControlType.SplitButton)))),
            TimeSpan.FromSeconds(15),
            "dialog button not found");
        ok.Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>
    /// Cancels the Windows print dialog. On Windows 11 the WPF PrintDialog shows the modern print UI hosted by the
    /// system (another process); older systems show the classic #32770 dialog.
    /// </summary>
    public void CancelPrintDialog()
    {
        var desktop = _automation.GetDesktop();
        AutomationElement? cancel = null;
        WaitUntil(
            () =>
            {
                var classic = MainWindow.FindFirstChild(c => c.ByClassName("#32770"));
                if (classic is not null)
                {
                    cancel = classic.FindFirstDescendant(c => c.ByAutomationId("2").And(c.ByControlType(ControlType.Button)));
                    return cancel is not null;
                }

                var modern = desktop.FindFirstChild(c => c.ByClassName("ApplicationFrameWindow").And(c.ByName("winPaint - Print")));
                cancel = modern?.FindFirstDescendant(c => c.ByName("Cancel").And(c.ByControlType(ControlType.Button)));
                return cancel is not null;
            },
            TimeSpan.FromSeconds(30),
            "print dialog did not appear");
        cancel!.Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>Presses Cancel in a Windows common dialog.</summary>
    public static void NativeCancel(AutomationElement dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var cancel = WaitFor(() => dialog.FindFirstDescendant(c => c.ByAutomationId("2").And(c.ByControlType(ControlType.Button))), TimeSpan.FromSeconds(15), "Cancel not found");
        cancel.Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>Waits for a descendant of <paramref name="parent"/> with the given automation id.</summary>
    public static AutomationElement WaitForElement(AutomationElement parent, string automationId, double timeoutSeconds = 15) =>
        WaitFor(() => parent.FindFirstDescendant(c => c.ByAutomationId(automationId)), TimeSpan.FromSeconds(timeoutSeconds), $"element '{automationId}' not found");

    /// <summary>Polls until a condition holds.</summary>
    public static void WaitUntil(Func<bool> condition, TimeSpan timeout, string? message = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException(message ?? "condition not met");
    }

    private static T WaitFor<T>(Func<T?> find, TimeSpan timeout, string? message = null)
        where T : class
    {
        T? result = null;
        WaitUntil(() => (result = find()) is not null, timeout, message);
        return result!;
    }

    private static T? Retry<T>(Func<T?> f, TimeSpan timeout)
        where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var r = f();
                if (r is not null)
                {
                    return r;
                }
            }
            catch (TimeoutException)
            {
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }

            Thread.Sleep(200);
        }

        return null;
    }
}
