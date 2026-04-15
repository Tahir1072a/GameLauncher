namespace GameLauncher.Services.Abstract
{
    /// <summary>
    /// Uygulamayı güvenli bir şekilde kapatma sorumluluğunu taşıyan arayüz.
    /// 
    /// Kapanmadan önce bekleyen logların gönderilmesini (IAppLifecycleService.FlushAsync)
    /// garanti eder. Bu sayede Application.Current.Shutdown() çağrıları tek bir noktada
    /// merkezileştirilir ve test edilebilir hale gelir.
    /// </summary>
    public interface IShutdownService
    {
        void Shutdown();
    }
}
