using System.Windows;
using System.Windows.Threading;
using FileDiff.Core.Storage;
using FileDiff.Core.Util;

namespace FileDiff.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureCreated();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Fatal error", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        AppLog.Info($"FileDiff started (v{typeof(App).Assembly.GetName().Version})");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI error", e.Exception);

        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were logged to:\n{AppPaths.LogFile}",
            "FileDiff",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("FileDiff stopped");
        base.OnExit(e);
    }
}
