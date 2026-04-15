using GameLauncher.Services.Abstract;
using GameLauncher.Services.Concrete;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        public IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddDebug(); // Outputa aktarıyoruz.

                // ILogger logları otomatik olarak sentry'e gider.
                builder.AddSentry(options =>
                {
                    options.Dsn = "https://cee4c274e6769124e2fa35c8145bb588@o4511211760123904.ingest.de.sentry.io/4511211888836688";
                    options.MinimumBreadcrumbLevel = LogLevel.Information;
                    options.MinimumEventLevel = LogLevel.Warning;
                });
            });

            services.AddHttpClient<IVersionService, VersionService>(client =>
            {
                client.BaseAddress = new Uri("https://versioncontrolmanagement-production.up.railway.app");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(60);
            });

            services.AddTransient<IFileService, FileService>();
            services.AddTransient<IAppLauncherService, AppLauncherService>();
            services.AddHttpClient<INetworkService, NetworkService>(client =>
            {
                client.BaseAddress = new Uri("https://files.aviotest.org");
                client.Timeout = TimeSpan.FromSeconds(60);
            });
            services.AddTransient<ISystemUpdateService, SystemUpdateService>();
            services.AddTransient<IAppService, AppService>();

            services.AddSingleton<IAppLifecycleService, SentryLifecycleService>();
            services.AddSingleton<IShutdownService, ShutdownService>();

            services.AddTransient<MainWindow>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        /// <summary>
        /// Uygulama kapanırken tüm servisleri (Logging provider'lar dahil) temizler.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }

        void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            try
            {
                var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
                logger.LogCritical(e.Exception, "Yakalanmamış/Beklenmeyen UI thread hatası");
            }
            catch
            {
                // Logger erişilemezse sessizce geç
            }

            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n\n{e.Exception.Message}",
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);

            var shutdownService = ServiceProvider.GetRequiredService<IShutdownService>();
            shutdownService.Shutdown();
        }
    }
}
