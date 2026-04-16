using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System.Windows;

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

        public ShutdownService(IAppLifecycleService lifecycle, ILogger<ShutdownService> logger)
        {
            _lifecycle = lifecycle;
            _logger = logger;
        }

        public void Shutdown()
        {
            _logger.LogInformation("Uygulama kapatılıyor, loglar gönderiliyor...");

           try
            {
                var flushTask = _lifecycle.FlushAsync();

                if (!flushTask.Wait(TimeSpan.FromSeconds(3)))
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
                Application.Current.Shutdown();
            }
            finally
            {
                Environment.Exit(0);
            }
        }
    }
}
