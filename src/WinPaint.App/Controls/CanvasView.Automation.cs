using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using WinPaint.Core.Tools;

namespace WinPaint.App.Controls;

/// <summary>
/// UI Automation support for the canvas. Besides its name, the canvas exposes a Value pattern that accepts pointer
/// scripts (in canvas pixels) so automation clients and assistive tools can draw without a physical mouse, and
/// returns a short state summary. Example: <c>drag 10 10 200 80 left shift; dblclick 30 30</c>.
/// </summary>
public sealed partial class CanvasView
{
    /// <summary>Handles non-pointer automation commands (keys, snapshots). Returns true when handled.</summary>
    public Func<string, bool>? ExternalAutomationCommand { get; set; }

    /// <summary>Supplies the automation state summary.</summary>
    public Func<string>? AutomationState { get; set; }

    private bool _injectedDrag;

    /// <summary>Number of automation scripts completed (so clients can wait for asynchronous execution).</summary>
    public int AutomationCompleted { get; private set; }

    /// <summary>Last automation error (empty when the last script succeeded).</summary>
    public string AutomationError { get; private set; } = string.Empty;

    /// <summary>
    /// Queues a script for execution on the UI thread and returns immediately (commands may open modal dialogs).
    /// </summary>
    public void QueueAutomation(string script) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
    {
        try
        {
            ExecuteAutomation(script);
            AutomationError = string.Empty;
        }
        catch (ArgumentException ex)
        {
            AutomationError = ex.Message;
        }
        finally
        {
            AutomationCompleted++;
        }
    });

    /// <summary>Executes an automation script (commands separated by ';' or new lines).</summary>
    public void ExecuteAutomation(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        foreach (var raw in script.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ExecutePointerCommand(raw) && ExternalAutomationCommand?.Invoke(raw) != true)
            {
                throw new ArgumentException($"Unknown automation command: {raw}", nameof(script));
            }

            UpdateLayout();
            FlushRendering();
        }
    }

    private bool ExecutePointerCommand(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return true;
        }

        var verb = parts[0].ToLowerInvariant();
        var numbers = parts.Skip(1).Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (double?)d : null).TakeWhile(d => d is not null).Select(d => d!.Value).ToList();
        var words = parts.Skip(1 + numbers.Count).Select(w => w.ToLowerInvariant()).ToList();
        var button = words.Contains("right") ? PointerButton.Right : words.Contains("middle") ? PointerButton.Middle : PointerButton.Left;
        var mods = ModifierKeys.None;
        if (words.Contains("shift"))
        {
            mods |= ModifierKeys.Shift;
        }

        if (words.Contains("ctrl"))
        {
            mods |= ModifierKeys.Control;
        }

        var space = words.Contains("space");
        Point P(int i) => _vt.CanvasToView(new Point(numbers[i], numbers[i + 1]));
        _injectedDrag = verb is "down" or "move" or "click" or "dblclick" or "drag" || (_injectedDrag && verb != "up");
        try
        {
            return ExecutePointerVerb(verb, numbers, words, button, mods, space, P);
        }
        finally
        {
            if (verb is "up" or "click" or "dblclick" or "drag")
            {
                _injectedDrag = false;
            }
        }
    }

    private bool ExecutePointerVerb(string verb, List<double> numbers, List<string> words, PointerButton button, ModifierKeys mods, bool space, Func<int, Point> P)
    {
        switch (verb)
        {
            case "down" when numbers.Count >= 2:
                HandleDown(P(0), button, mods, words.Contains("double") ? 2 : 1, space);
                return true;
            case "move" when numbers.Count >= 2:
                HandleMove(P(0), mods);
                return true;
            case "up" when numbers.Count >= 2:
                HandleUp(P(0), button, mods);
                return true;
            case "click" when numbers.Count >= 2:
                HandleDown(P(0), button, mods, 1, space);
                HandleUp(P(0), button, mods);
                return true;
            case "dblclick" when numbers.Count >= 2:
                HandleDown(P(0), button, mods, 1, space);
                HandleUp(P(0), button, mods);
                HandleDown(P(0), button, mods, 2, space);
                HandleUp(P(0), button, mods);
                return true;
            case "drag" when numbers.Count >= 4 && numbers.Count % 2 == 0:
            {
                // The whole path is mapped to screen positions up front (like a physical mouse drag), so panning
                // during the drag does not feed back into the path. Long segments are interpolated.
                var path = new List<Point>();
                for (var i = 0; i < numbers.Count; i += 2)
                {
                    path.Add(P(i));
                }

                HandleDown(path[0], button, mods, 1, space);
                for (var i = 1; i < path.Count; i++)
                {
                    var a = path[i - 1];
                    var b = path[i];
                    var steps = Math.Max(1, (int)((b - a).Length / 5));
                    for (var s = 1; s <= steps; s++)
                    {
                        HandleMove(a + ((b - a) * s / steps), mods);
                    }
                }

                HandleUp(path[^1], button, mods);
                return true;
            }
            case "hover" when numbers.Count >= 2:
                HandleMove(P(0), mods);
                return true;
            case "tick":
                Controller?.Tick();
                return true;
            case "wheel" when numbers.Count >= 3:
                if (words.Contains("ctrl"))
                {
                    SetZoom(numbers[2] > 0 ? Core.View.ViewTransform.NextPreset(_vt.Zoom) : Core.View.ViewTransform.PreviousPreset(_vt.Zoom), P(0));
                }
                else
                {
                    ScrollBy(words.Contains("shift") ? -numbers[2] / 120 * 48 : 0, words.Contains("shift") ? 0 : -numbers[2] / 120 * 48);
                }

                return true;
        }

        return false;
    }

    /// <summary>Automation peer for the viewport exposing the Value pattern.</summary>
    private sealed class CanvasAutomationPeer(FrameworkElement owner, CanvasView view) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        public bool IsReadOnly => false;

        public string Value => $"seq={view.AutomationCompleted}|error={view.AutomationError}|{view.AutomationState?.Invoke()}";

        public void SetValue(string value) => view.QueueAutomation(value);

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

        protected override string GetClassNameCore() => "WinPaintCanvas";

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
