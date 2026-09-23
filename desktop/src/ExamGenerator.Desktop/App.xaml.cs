using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace ExamGenerator.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : WpfApplication
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Fallback sang Software rendering trên Windows 7 / Windows 8 để tránh lỗi driver đồ họa hoặc DirectX
        if (Environment.OSVersion.Version.Major < 10)
        {
            try
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            }
            catch { }
        }

        // Bắt lỗi toàn cục của luồng Dispatcher UI - ngăn chặn tuyệt đối lỗi APPCRASH / Stop working
        DispatcherUnhandledException += (s, args) =>
        {
            LogAndShowException("Giao diện UI (Dispatcher)", args.Exception);
            args.Handled = true; // Ngăn không cho Windows tắt ngang ứng dụng
        };

        // Bắt lỗi không bắt được từ AppDomain
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogAndShowException("Miền thực thi (AppDomain)", ex);
            }
        };

        // Bắt lỗi từ các Task bất đồng bộ chạy nền
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogAndShowException("Tiến trình nền (TaskScheduler)", args.Exception);
            args.SetObserved();
        };
    }

    private static void LogAndShowException(string source, Exception ex)
    {
        var errorDetails = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] LỖI HỆ THỐNG ({source}):\n\n" +
                           $"Loại lỗi: {ex.GetType().FullName}\n" +
                           $"Thông báo: {ex.Message}\n";

        if (ex.InnerException != null)
        {
            errorDetails += $"\nChi tiết lỗi con (InnerException): {ex.InnerException.Message}\n";
        }

        errorDetails += $"\nVết thực thi (StackTrace):\n{ex.StackTrace}";

        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            File.AppendAllText(logPath, errorDetails + "\n" + new string('=', 80) + "\n\n");
        }
        catch { }

        MessageBox.Show(errorDetails, "Cảnh báo Lỗi Hệ Thống", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
