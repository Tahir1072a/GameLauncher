using GameLauncher.Services.Abstract;
using Sentry;
using System.Threading;
using System.Threading.Tasks;

namespace GameLauncher.Services.Concrete
{
    /// <summary>
    /// Sentry SDK'ya özel flush implementasyonu.
    /// 
    /// SentrySdk referansı tüm projede yalnızca bu dosyada bulunur.
    /// Vendor değişikliğinde sadece bu sınıf değiştirilir veya yenisiyle değiştirilir.
    /// </summary>
    public class SentryLifecycleService : IAppLifecycleService
    {
        public async Task FlushAsync(CancellationToken ct = default)
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(5));
        }
    }
}
