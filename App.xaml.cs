using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PrinterTool;

public partial class App : Application
{
    public static Settings Settings { get; } = Settings.Load();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Any unhandled failure must be visible and logged, never a silent exit.
        DispatcherUnhandledException += (_, args) =>
        {
            Report(args.Exception);
            args.Handled = true;
            // A failure before the window ever appeared must end the process —
            // otherwise an invisible PrinterConnect.exe keeps running.
            if (MainWindow is not { IsLoaded: true }) Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report(args.ExceptionObject as Exception);
        // Fire-and-forget tasks: log instead of losing the error silently (no UI —
        // this runs on the finalizer thread).
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            // Timed-out SNMP/probe socket reads are abandoned on purpose and fault when the
            // socket closes — expected, not errors. error.log stays crash-only.
            bool expected = args.Exception.Flatten().InnerExceptions.All(x =>
                x is ObjectDisposedException or System.Net.Sockets.SocketException or OperationCanceledException);
            if (!expected) Report(args.Exception, showUi: false);
        };

        Loc.Init(Settings.Language);
        base.OnStartup(e);
        // The saved Light/Dark choice wins over the Windows setting from the first frame.
        bool dark = Settings.Theme switch { "light" => false, "dark" => true, _ => ThemeManager.IsDark() };
        ThemeManager.Apply(dark);
    }

    private static void Report(Exception? ex, bool showUi = true)
    {
        if (ex is null) return;
        string path = "";
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrinterConnect");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "error.log");
            File.AppendAllText(path, $"{DateTime.Now:u}  {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }

        if (!showUi) return;
        try
        {
            MessageBox.Show(
                $"{ex.GetType().Name}: {ex.Message}" +
                (path.Length > 0 ? $"{Environment.NewLine}{Environment.NewLine}Log: {path}" : ""),
                PrinterTool.MainWindow.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Settings.Save();
        base.OnExit(e);
    }
}
