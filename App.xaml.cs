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
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report(args.ExceptionObject as Exception);

        Loc.Init(Settings.Language);
        base.OnStartup(e);
        ThemeManager.Apply(ThemeManager.IsDark());
    }

    private static void Report(Exception? ex)
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
