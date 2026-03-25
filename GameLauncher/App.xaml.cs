using System.Configuration;
using System.Data;
using System.Windows;

namespace GameLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            //DispatcherUnhandledException += (s, ex) =>
            //{
            //    LogError(ex.Exception);
            //    ex.Handled = true; // Uygulamayı çökertme
            //    ShowFriendlyError("Beklenmeyen bir hata oluştu. Uygulama yeniden başlatılıyor...");
            //    RestartApp();
            //};


        }
    }

    public enum ErrorSeverity
    {
        Silent,
        Warning,
        Critical
    }

}
