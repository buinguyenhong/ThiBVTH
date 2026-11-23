using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ExamGenerator.Client;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            if (Environment.OSVersion.Version.Major < 10)
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            }
        }
        catch { /* ignore render mode fallback errors */ }

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"Đã xảy ra lỗi không mong muốn: {args.Exception.Message}", "ExamGenerator Client", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogCrash("UnhandledException", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client_crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}{Environment.NewLine}");
        }
        catch { /* ignore file logging errors */ }
    }
}
