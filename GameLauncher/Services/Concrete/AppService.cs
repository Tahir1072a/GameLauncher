using GameLauncher.Entites;
using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace GameLauncher.Services.Concrete
{
    public class AppService : IAppService
    {
        private const string APP_NAME = "avio-test";

        private readonly ILogger<AppService> _logger;
        private readonly INetworkService _networkService;
        private readonly IFileService _fileService;

        public AppService(ILogger<AppService> logger, INetworkService networkService, IFileService fileService)
        {
            _logger = logger;
            _networkService = networkService;
            _fileService = fileService;
        }

        public bool RequiresUpdate(AppVersionDto? newVersion, AppVersionDto currentVersion)
        {
            if (newVersion == null) return false;

            if (newVersion.Critical) return true; // Fallback / Downgrade
            return newVersion.Version > currentVersion.Version;
        }

        public bool IsMandatoryUpdate(AppVersionDto? newVersion, AppVersionDto currentVersion)
        {
            if (newVersion == null) return false;

            if (newVersion.Critical || newVersion.IsMandatory) return true;

            if (newVersion.MinVersion != null && newVersion.MinVersion > currentVersion.Version)
            {
                return true;
            }

            return false;
        }

        public async Task<bool> DownloadAndInstallUpdateAsync(
            AppVersionDto gameVersion,
            ServerManifest localManifest,
            string gameDirectory,
            string backupDirectory,
            IProgress<DownloadState> progressReporter)
        {
            var tempFileName = $"{APP_NAME}_{Guid.NewGuid()}_update.zip";
            var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);

            int maxRetryCount = 3;
            bool isVerified = false;

            try
            {
                for (int attempt = 1; attempt <= maxRetryCount; attempt++)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(gameVersion.DownloadUrl))
                        {
                            throw new UpdateFailedException("Oyun indirme url'i boş olamaz.");
                        }

                        _logger.LogInformation("Oyun indirme işlemi için yer kontrolü yapılıyor.");
                        _fileService.IsEnoughSpaceOnDisk(gameVersion.Size ?? 0, tempZipPath);

                        // We rely on caller to pass a progress reporter, the caller might inject "Yeniden indiriliyor" text directly in the progress reporter's wrapper, but here we just pass it along
                        await _networkService.DownloadFilesAsync(gameVersion.DownloadUrl, tempZipPath, progressReporter);

                        if (string.IsNullOrEmpty(gameVersion.ChecksumSha256))
                        {
                            _logger.LogWarning("Sunucudan hash bilgisi gelmedi, doğrulama atlanıyor.");
                            isVerified = true;
                        }
                        else
                        {
                            isVerified = await _fileService.VerifyFileHashAsync(tempZipPath, gameVersion.ChecksumSha256);
                        }

                        if (isVerified)
                        {
                            break;
                        }
                        else
                        {
                            _logger.LogWarning("Bozuk uygulama dosyası tespit edildi, silinip yeniden denenecek. Deneme: {Attempt}", attempt);
                            await _fileService.DeleteFileAsync(tempZipPath);
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
                    {
                        _logger.LogWarning(ex, "Uygulama indirilirken ağ veya I/O kesintisi yaşandı. Deneme: {Attempt}", attempt);
                        if (attempt == maxRetryCount) throw new UpdateFailedException("Bağlantı kesintisi nedeniyle dosya 3 kez indirilmesine rağmen tamamlanamadı. Lütfen bağlantınızı kontrol edin.", ex);
                        await Task.Delay(1000);
                    }
                }

                if (!isVerified)
                {
                    throw new UpdateFailedException("Güncelleme dosyası 3 kez indirilmesine rağmen doğrulanamadı. İnternet bağlantınızda veya diskte bir sorun olabilir.");
                }

                bool backupSuccessful = false;
                try
                {
                    // Yedekleme 
                    _logger.LogInformation("Uygulama klasörü yedekleniyor...");
                    await _fileService.CreateBackupAsync(gameDirectory, backupDirectory);
                    backupSuccessful = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Yedekleme sırasında hata oluştu. Kurulum yedek alınmadan devam edecek.");
                }

                try
                {
                    // Temizleme 
                    _logger.LogInformation("Uygulama klasörü temizleniyor...");
                    await _fileService.CleanDirectoryAsync(gameDirectory);

                    // Kurulum
                    _logger.LogInformation("Uygulama klasörüne çıkartılıyor...");
                    await _fileService.ExtractZipAsync(tempZipPath, gameDirectory, overwrite: true);
                }
                catch (Exception extractionEx)
                {
                    _logger.LogError(extractionEx, "Çıkarma veya temizleme sırasında hata oluştu. Geri alma (Rollback) durumu değerlendiriliyor.");
                    
                    if (backupSuccessful)
                    {
                        try
                        {
                            _logger.LogInformation("Yedekten geri yükleniyor...");
                            await _fileService.RestoreBackupAsync(backupDirectory, gameDirectory);

                            throw new UpdateRolledBackException(
                                "Oyun güncellenirken bir sorun oluştu fakat eski dosyalara başarılı bir şekilde geri dönüldü.\n\n" +
                                "Mevcut sürümünüzle oyuna giriş yapabilirsiniz.");
                        }
                        catch (Exception restoreEx)
                        {
                            _logger.LogCritical(restoreEx, "Kritik Hata: Kurulum başarısız oldu ve yedekten geri dönüş yapılamadı!");
                            
                            throw new Exception(
                                "Oyun güncellenirken kritik bir hata oluştu ve işlem yarım kaldı.\n\n" +
                                "Lütfen uygulamayı baştan çalıştırarak Otomatik Onarım'ın devreye girmesini bekleyin veya oyunu sıfırdan kurmayı deneyin.", restoreEx);
                        }
                    }
                    else
                    {
                        throw new Exception(
                                "Oyun güncellenirken kritik bir hata oluştu ve işlem yarım kaldı.\n\n" +
                                "Lütfen uygulamayı baştan çalıştırarak Otomatik Onarım'ın devreye girmesini bekleyin veya oyunu sıfırdan kurmayı deneyin.", extractionEx);
                    }
                }

                localManifest.game = gameVersion;
                return true;
            }
            finally
            {
                await _fileService.DeleteFileAsync(tempZipPath);
            }
        }
    }
}
