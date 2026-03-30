using System;

namespace GameLauncher.Entites
{
    /// <summary>
    /// Servis katmanlarında (AppService, SystemUpdateService) gerçekleşen ve uygulamanın çökmesini (Crash) 
    /// gerektirmeyen, salt indirme/doğrulama veya dosya kurma başarısızlıkları için özel tanımlanmış Exception tipi.
    /// MainWindow içerisindeki Global Exception Controller, bu tipteki hataları yakaladığında uygulamayı Shutdown() yapmak
    /// yerine kullanıcıya uyarı gösterip Launcher'ı Çevrimdışı/Yeniden Dene ("İsOffline") moduna sokarak kilitlenmeyi engeller.
    /// </summary>
    public class UpdateFailedException : Exception
    {
        public UpdateFailedException() { }

        public UpdateFailedException(string message) : base(message) { }

        public UpdateFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
}
