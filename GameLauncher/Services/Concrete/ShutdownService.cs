using GameLauncher.Services.Abstract;
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

        public ShutdownService(IAppLifecycleService lifecycle)
        {
            _lifecycle = lifecycle;
        }

        public void Shutdown()
        {
            try
            {
                _lifecycle.FlushAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Flush başarısız olsa bile uygulamayı kapat
            }

            Application.Current.Shutdown();
        }
    }
}
