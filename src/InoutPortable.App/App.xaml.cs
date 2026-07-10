using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using InoutPortable.Core.Infrastructure;

namespace InoutPortable.App;

/// <summary>
/// Interaction logic for App.xaml. Installs global exception handlers so an unexpected error shows a
/// message and gets logged to data\crash.log instead of silently closing the app.
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception, "Task"); e.SetObserved(); };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception, "Dispatcher");
        MessageBox.Show(
            "Se ha producido un error inesperado:\n\n" + e.Exception.Message +
            "\n\nSe ha registrado el detalle en:\n" + CrashLogPath,
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the app alive
    }

    private static string CrashLogPath => Path.Combine(AppPaths.DataDirectory, "crash.log");

    internal static void Log(Exception? ex, string source)
    {
        if (ex is null) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] =====");
            sb.AppendLine(ex.ToString());
            File.AppendAllText(CrashLogPath, sb.ToString());
        }
        catch { /* never let logging throw */ }
    }
}
