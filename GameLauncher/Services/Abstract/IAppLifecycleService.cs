using System.Threading;
using System.Threading.Tasks;

namespace GameLauncher.Services.Abstract
{
    /// <summary>
    /// Log provider'ların (Sentry, Seq, Datadog vb.) bekleyen loglarını
    /// göndermesini (flush) sağlayan yaşam döngüsü arayüzü.
    /// 
    /// Bu arayüz sayesinde uygulama kodunda hiçbir vendor-specific referans bulunmaz.
    /// Vendor değişikliği sadece DI kaydını ve implementasyon dosyasını etkiler.
    /// </summary>
    public interface IAppLifecycleService
    {
        Task FlushAsync(CancellationToken ct = default);
    }
}
