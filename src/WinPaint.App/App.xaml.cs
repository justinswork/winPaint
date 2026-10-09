using System.Windows;
using System.Windows.Threading;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.App.ViewModels;
using WinPaint.App.Views;

namespace WinPaint.App;

/// <summary>Application entry: settings, theme, crash handling, command-line file.</summary>
public partial class App : Application
{
    private MainViewModel? _vm;
    private bool _crashing;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            args.SetObserved();
        };

        var settings = new SettingsService();
        settings.Load();
        ThemeManager.Apply(settings.Current.Theme);

        var window = new MainWindow();
        var dialogs = new DialogService(window, settings);
        _vm = new MainViewModel(settings, dialogs, new ClipboardService());
        _vm.ThemeChanged += (_, _) => ThemeManager.Apply(_vm.Theme);
        dialogs.ThemeSetter = t => _vm.Theme = t;
        window.Attach(_vm, settings);
        MainWindow = window;
        window.Show();

        var file = e.Args.FirstOrDefault(a => !a.StartsWith('-'));
        if (file is not null)
        {
            _ = _vm.OpenPathAsync(file);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var log = CrashLog.Write(e.Exception);
        if (_crashing)
        {
            return;
        }

        _crashing = true;
        e.Handled = true;
        try
        {
            var message = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Crash, log);
            var save = MessageDialog.Show(MainWindow, Strings.Msg_CrashTitle, message, [Strings.Yes, Strings.No], 0) == 0;
            if (save && _vm is not null)
            {
                try
                {
                    var path = _vm.SaveRecoveryCopy();
                    MessageDialog.Show(MainWindow, Strings.Msg_CrashTitle, string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_RecoverySaved, path), [Strings.OK], 0);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    CrashLog.Write(ex);
                }
            }
        }
        finally
        {
            Shutdown(1);
        }
    }
}

/// <summary>Writes crash logs to %LocalAppData%\winPaint\logs.</summary>
public static class CrashLog
{
    /// <summary>Writes an exception and returns the log path.</summary>
    public static string Write(Exception? ex)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winPaint", "logs");
        var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, $"winPaint {typeof(App).Assembly.GetName().Version}{Environment.NewLine}{DateTime.Now:O}{Environment.NewLine}{ex}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return path;
    }
}
