using Sentry;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace GameLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            SentrySdk.Init(o =>
            {
                o.Dsn = "https://0754fa7c132b8513c40211572bc4147e@o4511103639683072.ingest.de.sentry.io/4511103841927248";
                // When configuring for the first time, to see what the SDK is doing:
                o.Debug = true;
            });

            // Background thread hataları
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                SentrySdk.CaptureException(ex.ExceptionObject as Exception);
            };

            // async/await hataları
            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                SentrySdk.CaptureException(ex.Exception);
                ex.SetObserved();
            };
        }

        void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            SentrySdk.CaptureException(e.Exception);
            e.Handled = true;
        }
    }
}
