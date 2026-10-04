using System.Windows;
using WinInstagram.Services;

namespace WinInstagram;

public partial class App : Application
{
    public static string InitialLaunchUrl { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!DeepLinkService.Instance.CheckSingleInstanceAndForward(e.Args))
        {
            Shutdown();
            return;
        }

        if (e.Args.Length > 0)
        {
            InitialLaunchUrl = DeepLinkService.NormalizeInstagramUrl(string.Join(" ", e.Args));
        }

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

    protected override void OnExit(ExitEventArgs e)
    {
        DeepLinkService.Instance.Shutdown();
        base.OnExit(e);
    }
}
