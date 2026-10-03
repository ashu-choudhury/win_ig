using System.Windows;
using WinInstagram.Services;

namespace WinInstagram;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            AppLogger.Error("CRASH", "AppDomain UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            AppLogger.Error("CRASH", "DispatcherUnhandledException: " + args.Exception.Message, args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            AppLogger.Error("CRASH", "UnobservedTaskException: " + args.Exception.Message, args.Exception);
            args.SetObserved();
        };

        AppLogger.Info("APP", "=== WinInstagram Application Starting ===");
    }
}
