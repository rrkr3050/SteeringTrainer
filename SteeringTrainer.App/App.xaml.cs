using System.Windows;

namespace SteeringTrainer.App
{
    public partial class App : Application
    {
        public App()
        {
            AppLog.Write($"Process starting. Runtime={Environment.Version}; BaseDirectory={AppContext.BaseDirectory}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                AppLog.Write($"Fatal exception (terminating={e.IsTerminating}): {e.ExceptionObject}");
            DispatcherUnhandledException += (_, e) =>
            {
                e.Handled = true;
                ReportFailure(e.Exception);
            };
            TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Write($"Unobserved task exception: {e.Exception}");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                AppLog.Write("Creating main window.");
                MainWindow = new MainWindow();
                MainWindow.ContentRendered += (_, _) => AppLog.Write("Main window rendered. Startup completed.");
                MainWindow.Show();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        private void ReportFailure(Exception exception)
        {
            AppLog.Write($"Application failure: {exception}");
            MessageBox.Show($"アプリでエラーが発生しました。\n\n{exception.Message}\n\nログ: {AppLog.FilePath}",
                "Steering Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppLog.Write($"Process exiting. ExitCode={e.ApplicationExitCode}");
            base.OnExit(e);
        }
    }
}
