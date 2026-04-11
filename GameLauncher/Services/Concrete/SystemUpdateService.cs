using GameLauncher.Entites;
using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GameLauncher.Services.Concrete
{
    public class SystemUpdateService : ISystemUpdateService
    {
        private const string UPDATER_NAME = "Updater";
        private const string UPDATER_EXE = "Updater.exe";
        private const string LAUNCHER_NAME = "GameLauncher";

        private readonly ILogger<SystemUpdateService> _logger;
        private readonly INetworkService _networkService;
        private readonly IFileService _fileService;

        public SystemUpdateService(ILogger<SystemUpdateService> logger, INetworkService networkService, IFileService fileService)
        {
            _logger = logger;
            _networkService = networkService;
            _fileService = fileService;
        }

        public async Task<bool> CheckAndUpdateSystemComponentsAsync(
            AppVersionDto? launcherVersion,
            AppVersionDto? updaterVersion,
            ServerManifest localManifest,
            string appDirectory,
            string updaterDirectory,
            IProgress<DownloadState> progressReporter)
        {
            // 1. Updater Güncellemesi Kontrolü
            bool updaterRequiresUpdate = ShouldUpdate(updaterVersion, localManifest.updater);
            if (updaterRequiresUpdate)
            {
                _logger.LogInformation("Updater için yeni bir sürüm bulundu veya zorunlu düşürme(downgrade) istendi.");
                await UpdateUpdaterAsync(updaterVersion!, localManifest, appDirectory, updaterDirectory, progressReporter);
            }

            // 2. Launcher Güncellemesi Kontrolü
            bool launcherRequiresUpdate = ShouldUpdate(launcherVersion, localManifest.launcher);
            if (launcherRequiresUpdate)
            {
                _logger.LogInformation("Launcher için yeni bir sürüm bulundu veya zorunlu düşürme(downgrade) istendi.");
                await UpdateLauncherAsync(launcherVersion!, appDirectory, updaterDirectory, progressReporter);
                return true;
            }

            return false;
        }

        private bool ShouldUpdate(AppVersionDto? newVersion, AppVersionDto currentVersion)
        {
            if (newVersion == null) return false;

            if (newVersion.Critical) return true;

            return newVersion.Version > currentVersion.Version;
        }

        private async Task UpdateUpdaterAsync(
            AppVersionDto updaterVersion,
            ServerManifest localManifest,
            string appDirectory,
            string updaterDirectory,
            IProgress<DownloadState> progressReporter)
        {
            var tempFileName = $"{UPDATER_NAME}_{Guid.NewGuid()}_update.zip";
            var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);

            try
            {
                if (string.IsNullOrEmpty(updaterVersion.DownloadUrl))
                {
                    throw new UpdateFailedException("Updater indirme url'i boş.");
                }

                _fileService.IsEnoughSpaceOnDisk(updaterVersion.Size ?? 0, tempZipPath);

                await _networkService.DownloadFilesAsync(updaterVersion.DownloadUrl, tempZipPath, progressReporter);

                if (Directory.Exists(updaterDirectory))
                {
                    Directory.Delete(updaterDirectory, true);
                }

                await _fileService.ExtractZipAsync(tempZipPath, updaterDirectory);

                // Geriye dönük uyumluluk: Eski sistemlerde Updater ana dizine kuruluyordu, onları temizle
                await CleanupOldUpdaterFiles(appDirectory);

                localManifest.updater.Version = updaterVersion.Version;
                localManifest.updater.DownloadUrl = updaterVersion.DownloadUrl;
                localManifest.updater.ChecksumSha256 = updaterVersion.ChecksumSha256;
                localManifest.updater.Size = updaterVersion.Size;
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { /* Windows halleder */ }
                }
            }
        }

        private async Task UpdateLauncherAsync(
            AppVersionDto launcherVersion,
            string appDirectory,
            string updaterDirectory,
            IProgress<DownloadState> progressReporter)
        {
            var tempFileName = $"{LAUNCHER_NAME}_{Guid.NewGuid()}_launcher.zip";
            var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);

            try
            {
                if (string.IsNullOrEmpty(launcherVersion.DownloadUrl))
                {
                    throw new UpdateFailedException("Launcher indirme url'i boş.");
                }

                _fileService.IsEnoughSpaceOnDisk(launcherVersion.Size ?? 0, tempZipPath);

                await _networkService.DownloadFilesAsync(launcherVersion.DownloadUrl, tempZipPath, progressReporter);

                string updaterExe = Path.Combine(updaterDirectory, UPDATER_EXE);

                if (!File.Exists(updaterExe))
                {
                    throw new FileNotFoundException($"Kritik Hata: Updater.exe bulunamadı: {updaterExe}");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = $"/mode:launcher /source:\"{tempZipPath}\" /target:\"{appDirectory}\" /pid:{Process.GetCurrentProcess().Id}",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(updaterExe)
                };

                const int maxAttempts = 3;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    _logger.LogInformation("Updater.exe başlatılıyor... Deneme: {Attempt}/{Max}", attempt, maxAttempts);

                    var process = Process.Start(startInfo);

                    if (process == null)
                    {
                        _logger.LogWarning("Process.Start() null döndü. Deneme: {Attempt}/{Max}", attempt, maxAttempts);

                        if (attempt == maxAttempts)
                            throw new UpdateFailedException("Updater.exe başlatılamadı. Process.Start() null döndü. Antivirüs yazılımınız engellemiş olabilir.");

                        await Task.Delay(500);
                        continue;
                    }

                    await Task.Delay(500 * attempt);

                    if (process.HasExited)
                    {
                        _logger.LogWarning(
                            "Updater.exe başlatıldı ama hemen kapandı. ExitCode: {ExitCode}, Deneme: {Attempt}/{Max}",
                            process.ExitCode, attempt, maxAttempts);

                        if (attempt == maxAttempts)
                            throw new UpdateFailedException(
                                $"Updater.exe {maxAttempts} denemede de başlatıldıktan hemen sonra kapandı (ExitCode: {process.ExitCode}). " +
                                "Antivirüs programınız engellemiş olabilir veya sistem dosyalarında bir sorun var.");

                        await Task.Delay(500);
                        continue;
                    }

                    _logger.LogInformation("Updater.exe başarıyla başlatıldı ve çalışıyor. PID: {PID}", process.Id);
                    return;
                }
            }
            catch (UpdateFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Launcher güncelleme dosyası indirilemedi veya Updater başlatılamadı.");
                throw;
            }
        }

        private async Task CleanupOldUpdaterFiles(string appDirectory)
        {
            try
            {
                string[] oldFiles = {
                    UPDATER_EXE,
                    "Updater.pdb",
                    "Updater.deps.json",
                    "Updater.runtimeconfig.json"
                };

                foreach (var fileName in oldFiles)
                {
                    string filePath = Path.Combine(appDirectory, fileName);
                    await _fileService.DeleteFileAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Geriye dönük uyumluluk için aranan eski updater dosyaları (ana dizinde) silinemedi.");
            }
        }
    }
}
