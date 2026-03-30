using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace GameLauncher.Services.Concrete
{
    public class AppLauncherService : IAppLauncherService
    {
        private readonly ILogger<AppLauncherService> _logger;

        public AppLauncherService(ILogger<AppLauncherService> logger)
        {
            _logger = logger;
        }

        public AppLaunchResult LaunchApplication(string gameDirectory, string mainExeName)
        {
            var exePath = Path.Combine(gameDirectory, mainExeName);

            try
            {
                if (!File.Exists(exePath))
                {
                    _logger.LogWarning("Ana uygulama dosyası bulunamadı: {ExePath}", exePath);
                    return new AppLaunchResult
                    {
                        IsSuccess = false,
                        ErrorTitle = "Dosya Eksik",
                        ErrorMessage = "Oyun dosyası bulunamadı.\n\n" +
                                       "Dosya silinmiş veya Antivirüs programınız tarafından karantinaya alınmış olabilir.\n" +
                                       "Lütfen antivirüs ayarlarınızı kontrol edin veya uygulamayı yeniden indirin."
                    };
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = gameDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
                
                // Başarılı açılışta eski hata kayıtlarını sil
                ResetWin32ErrorCount(gameDirectory);
                ResetRepairCount(gameDirectory);
                
                return new AppLaunchResult { IsSuccess = true };
            }
            catch (Win32Exception ex)
            {
                int currentFails = GetWin32ErrorCount(gameDirectory);
                currentFails++;
                SaveWin32ErrorCount(gameDirectory, currentFails);

                _logger.LogError(ex, "Windows uygulamayı başlatmayı reddetti. Kalıcı Hata sayısı: {Count}", currentFails);

                int repairCount = GetRepairCount(gameDirectory);

                if (repairCount >= 1 && currentFails >= 2)
                {
                    return new AppLaunchResult
                    {
                        IsSuccess = false,
                        IsUnrepairableSystemError = true,
                        ErrorTitle = "Sistem Uyumsuzluğu / Antivirüs Engeli",
                        ErrorMessage = "Oyun dosyaları başarıyla onarılmasına rağmen bilgisayarınız uygulamanın belleğe yüklenmesini donanımsal veya sistemsel düzeyde reddediyor.\n\n" +
                                       "Sorun '%100 ihtimalle' bilgisayarınızdaki agresif bir Antivirüs korumasından veya desteklenmeyen bir İşletim Sisteminden kaynaklanmaktadır.\n" +
                                       "Geliştirici ekiple görüşün, oyunu baştan indirmek sorunu malesef çözmeyecektir."
                    };
                }
                else if (currentFails >= 2)
                {
                    return new AppLaunchResult
                    {
                        IsSuccess = false,
                        RequiresAutoRepair = true,
                        ErrorTitle = "Kritik Başlatma Hatası",
                        ErrorMessage = "Oyun dosyalarınız üst üste iki kez başlatılamadı.\n" +
                                       "Otomatik onarım süreci başlatılıyor. Lütfen dosyalar yeniden indirilene kadar bekleyin."
                    };
                }
                else
                {
                    return new AppLaunchResult
                    {
                        IsSuccess = false,
                        ErrorTitle = "Başlatma Engellendi",
                        ErrorMessage = "Oyun programı bir sebepten engellendi.\n\n" +
                                       "Antivirüs veya Windows Defender programınız oyunu zararlı zannediyor olabilir. " +
                                       "Lütfen antivirüs kalkanını geçici olarak kapatıp tekrar başlatmayı deneyin."
                    };
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Uygulama başlatmak için yetki alınamadı.");
                return new AppLaunchResult
                {
                    IsSuccess = false,
                    ErrorTitle = "Yetki Reddedildi",
                    ErrorMessage = "Oyunu başlatmak için yeterli işletim sistemi izniniz yok.\n\n" +
                                   "Lütfen Launcher'a sağ tıklayıp 'Yönetici Olarak Çalıştır' diyerek tekrar deneyin."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Uygulama başlatılamadı.");
                return new AppLaunchResult
                {
                    IsSuccess = false,
                    ErrorTitle = "Beklenmeyen Hata",
                    ErrorMessage = $"Meydana gelen bir hata sebebiyle uygulama başlatılamadı:\n\n{ex.Message}"
                };
            }
        }

        private int GetWin32ErrorCount(string gameDirectory)
        {
            var failFilePath = Path.Combine(gameDirectory, ".launch_fails");
            if (File.Exists(failFilePath))
            {
                if (int.TryParse(File.ReadAllText(failFilePath), out int count))
                    return count;
            }
            return 0;
        }

        private void SaveWin32ErrorCount(string gameDirectory, int count)
        {
            try
            {
                var failFilePath = Path.Combine(gameDirectory, ".launch_fails");
                File.WriteAllText(failFilePath, count.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hata sayacı dosyaya yazılamadı.");
            }
        }

        private void ResetWin32ErrorCount(string gameDirectory)
        {
            try
            {
                var failFilePath = Path.Combine(gameDirectory, ".launch_fails");
                if (File.Exists(failFilePath))
                {
                    File.Delete(failFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hata sayacı dosyası silinemedi.");
            }
        }
        public void MarkRepairCompleted(string gameDirectory)
        {
            try
            {
                var repairFilePath = Path.Combine(gameDirectory, ".repair_count");
                int current = GetRepairCount(gameDirectory);
                File.WriteAllText(repairFilePath, (current + 1).ToString());
                // Onarım başarıyla yapıldığına göre HATA SAYAÇLARIMIZI sıfırlayalım ki yeni bir hata alırsak tekrar saysın.
                ResetWin32ErrorCount(gameDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Onarım sayacı dosyaya yazılamadı.");
            }
        }

        private int GetRepairCount(string gameDirectory)
        {
            var repairFilePath = Path.Combine(gameDirectory, ".repair_count");
            if (File.Exists(repairFilePath))
            {
                if (int.TryParse(File.ReadAllText(repairFilePath), out int count))
                    return count;
            }
            return 0;
        }

        private void ResetRepairCount(string gameDirectory)
        {
            try
            {
                var repairFilePath = Path.Combine(gameDirectory, ".repair_count");
                if (File.Exists(repairFilePath))
                {
                    File.Delete(repairFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Onarım sayacı silinemedi.");
            }
        }
    }
}
