using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace GameLauncher.Services.Concrete
{
    /// <summary>
    /// Uygulamayı güvenli kapatma servisi.
    /// 
    /// Kapanmadan önce IAppLifecycleService üzerinden bekleyen logların
    /// flush edilmesini sağlar, ardından uygulamayı kapatır.
    /// </summary>
    public class ShutdownService : IShutdownService
    {
        private readonly IAppLifecycleService _lifecycle;
        private readonly ILogger<ShutdownService> _logger;

        /// <summary>
        /// <see cref="ShutdownService"/> sınıfının yeni bir örneğini başlatır.
        /// </summary>
        /// <param name="lifecycle">Uygulama yaşam döngüsü servisini temsil eder.</param>
        /// <param name="logger">Loglama servisini temsil eder.</param>
        public ShutdownService(IAppLifecycleService lifecycle, ILogger<ShutdownService> logger)
        {
            _lifecycle = lifecycle;
            _logger = logger;
        }

        /// <summary>
        /// Uygulamayı güvenli bir şekilde kapatma işlemini başlatır.
        /// Arayüz etkileşimini keser, bekleyen log gönderme işlemlerinin tamamlanmasını (en fazla 3 saniye) bekler
        /// ve ardından uygulamayı tamamen sonlandırır.
        /// </summary>
        public async void Shutdown()
        {
            try
            {
                _logger.LogInformation("Uygulama kapatılıyor, loglar gönderiliyor...");

                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (Application.Current.MainWindow != null)
                        {
                            Application.Current.MainWindow.IsHitTestVisible = false;
                            Mouse.OverrideCursor = Cursors.Wait;

                            if (Application.Current.MainWindow is MainWindow mainWindow)
                            {
                                mainWindow.UpdateStatusForShutdown();
                            }
                        }
                    });
                }

                try
                {
                    var flushTask = _lifecycle.FlushAsync();
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));

                    var completedTask = await Task.WhenAny(flushTask, timeoutTask);

                    if (completedTask == flushTask)
                    {
                        await flushTask;
                    }
                    else
                    {
                        _logger.LogWarning("Flush timeout oldu, zorla kapatılıyor.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Flush sırasında hata oluştu.");
                }

                try
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        Mouse.OverrideCursor = null;
                        Application.Current.Shutdown();
                    });
                }
                catch
                {
                }
            }
            finally
            {
                Environment.Exit(0);
            }
        }
    }
}
