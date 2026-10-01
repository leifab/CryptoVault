using System;
using System.Threading.Tasks;
using System.Windows;

namespace CryptoVault;

public partial class App : Application
{
    // Re-entrancy guard preventing cascade dialog loops if multiple threads fault while a crash modal is open.
    private static bool s_isShowingCrash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. UI thread unhandled exceptions
        DispatcherUnhandledException += (sender, args) =>
        {
            ShowCrashDialog(args.Exception, "UI Thread", isFatal: false);
            args.Handled = true;
        };

        // 2. Background thread unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            ShowCrashDialog(ex, "Background Thread", isFatal: args.IsTerminating);
        };

        // 3. Unobserved Task exceptions (Async/await)
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            ShowCrashDialog(args.Exception, "Async Task", isFatal: false);
            args.SetObserved();
        };

        ThemeService.Initialize();
    }

    private static void ShowCrashDialog(Exception? ex, string source, bool isFatal)
    {
        if (s_isShowingCrash) return;
        s_isShowingCrash = true;
        try
        {
            string title = isFatal ? "CryptoVault - Fatal Error" : "CryptoVault - Unhandled Error";
            string exType = ex?.GetType().FullName ?? "Unknown Exception";
            string message = ex?.Message ?? "No exception message provided.";
            string details = ex?.ToString() ?? "No details available.";

            string text = $"An unexpected error occurred in {source}.\n\n" +
                          $"Type: {exType}\n" +
                          $"Message: {message}\n\n" +
                          $"Details:\n{details}";

            MessageBox.Show(
                text,
                title,
                MessageBoxButton.OK,
                isFatal ? MessageBoxImage.Error : MessageBoxImage.Warning);
        }
        catch
        {
            // Fail-safe to avoid crash loops
        }
        finally
        {
            s_isShowingCrash = false;
        }
    }
}

