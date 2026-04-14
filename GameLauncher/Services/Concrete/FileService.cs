using GameLauncher.Exceptions;
using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GameLauncher.Services.Concrete
{
    public class FileService : IFileService
    {
        private const int ERROR_DISK_FULL = unchecked((int)0x80070070);
        private const int ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);

        private readonly ILogger<IFileService> _logger;

        public FileService(ILogger<IFileService> logger)
        {
            _logger = logger;
        }

        public Task CleanDirectoryAsync(string directoryPath)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    return;
                }

                DirectoryInfo dirInfo = new DirectoryInfo(directoryPath);
                var protectedExtensions = new[] { ".sav", ".save", ".dat", ".user" };
                var protectedFiles = new[] { "settings.ini", "config.cfg", "userdata.db", "config.json" };
                var protectedFolders = new[] { "Screenshots", "Saves", "Logs", "cache" };

                var errors = new List<Exception> { };

                foreach (FileInfo file in dirInfo.GetFiles())
                {
                    try
                    {
                        var ext = file.Extension.ToLower();
                        bool isProtected = protectedExtensions.Contains(ext) || protectedFiles.Contains(file.Name);

                        if (!isProtected)
                        {
                            if (file.IsReadOnly) file.IsReadOnly = false;
                            file.Delete();
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogWarning(ex, "Erişim reddedildi: {File}", file.FullName);
                        errors.Add(ex);
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "Dosya kullanılıyor veya disk hatası: {File}", file.FullName);
                        errors.Add(ex);
                    }
                }


                foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
                {
                    if (!protectedFolders.Contains(subDir.Name))
                    {
                        try
                        {
                            subDir.Delete(true);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.LogWarning(ex, "Alt dizine erişim reddedildi: {Dir}", subDir.FullName);
                            errors.Add(ex);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogWarning(ex, "Alt dizin silinemedi (dosya kilitli olabilir): {Dir}", subDir.FullName);
                            errors.Add(ex);
                        }
                    }
                }

                if (errors.Count > 0)
                    throw new FileServiceException(
                        "Bazı dosyalar veya dizinler kilitli olduğu için temizlenemedi. Oyunun kapalı olduğundan emin olun.",
                        FileServiceOperation.Clean,
                        directoryPath,
                        new AggregateException(errors));
            });
        }

        public Task RestoreBackupAsync(string backupPath, string targetPath)
        {
            return Task.Run(async () =>
            {
                if (!Directory.Exists(backupPath))
                {
                    _logger.LogWarning("Geçerli bir yedek dizini yok: {Path}", backupPath);
                    return;
                }

                // Hedefi tamamen temizle, eğer daha önce yarım kalmış kurulum vs varsa silinsin
                await CleanDirectoryAsync(targetPath);

                // Yedeği yerine geri kopyala
                CopyDirectoryRecursive(backupPath, targetPath);
                
                _logger.LogInformation("Yedek başarıyla geri yüklendi: {Target}", targetPath);
            });
        }

        public Task CreateBackupAsync(string sourcePath, string backupPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Oyunun ilk kez kurulduğuna işarettir bu. Yedekleme yapacak bir şey yoktur.
                    if (!Directory.Exists(sourcePath))
                        return;

                    if (Directory.Exists(backupPath))
                    {
                        Directory.Delete(backupPath, true);
                        _logger.LogInformation("Mevcut yedek dizini silindi: {Path}", backupPath);
                    }

                    Directory.CreateDirectory(backupPath);
                    CopyDirectoryRecursive(sourcePath, backupPath);

                    _logger.LogInformation("Yedek başarıyla oluşturuldu: {Path}", backupPath);
                }
                catch (FileNotFoundException ex)
                {
                    throw new FileServiceException(
                        "Kaynak dizin bulunamadı.",
                        FileServiceOperation.Backup,
                        sourcePath, ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new FileServiceException(
                        "Yedek dizinine erişim reddedildi.",
                        FileServiceOperation.Backup,
                        backupPath, ex);
                }
                catch (IOException ex)
                {
                    throw new FileServiceException(
                        "Yedekleme sırasında disk hatası oluştu.",
                        FileServiceOperation.Backup,
                        backupPath, ex);
                }
            });
        }

        public Task DeleteFileAsync(string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Dosya başarıyla silindi: {File}", filePath);
                    }
                    else
                    {
                        _logger.LogDebug("Silinmek istenen dosya zaten mevcut değil: {File}", filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dosya silinirken beklenmeyen bir hata oluştu: {File}", filePath);
                    throw new FileServiceException("Dosya silinemedi.", FileServiceOperation.Delete, filePath, ex);
                }
            });
        }

        public Task ExtractZipAsync(string sourceArchiveFilePath, string extractPath, bool overwrite = true)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(sourceArchiveFilePath))
                    throw new FileServiceException("Kaynak arşiv dosyası bulunamadı.", FileServiceOperation.Extract, sourceArchiveFilePath);

                if (!Directory.Exists(extractPath))
                {
                    Directory.CreateDirectory(extractPath);
                }

                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        ZipFile.ExtractToDirectory(sourceArchiveFilePath, extractPath, overwrite);
                        _logger.LogInformation("Arşiv başarıyla çıkarıldı: {Path}", extractPath);
                        return;
                    }
                    catch (InvalidDataException ex)
                    {
                        _logger.LogError(ex, "Bozuk veya geçersiz bir arşiv dosyası: {Path}", sourceArchiveFilePath);
                        throw new FileServiceException("Bozuk veya geçersiz bir arşiv dosyası.", FileServiceOperation.Extract, sourceArchiveFilePath, ex);
                    }
                    catch (IOException ex) when (attempt < maxRetries)
                    {
                        _logger.LogWarning(ex, $"Dosya kilitli, {attempt * 1000}ms sonra yeniden denenecek. Deneme: {attempt}/{maxRetries}");
                        Thread.Sleep(attempt * 1000);
                    }
                    catch (IOException ex) when (ex.HResult == ERROR_DISK_FULL || ex.HResult == ERROR_HANDLE_DISK_FULL)
                    {
                        _logger.LogError(ex, "Diskte yeterli alan kalmadı. {Path}", extractPath);
                        throw new FileServiceException("Oyun dosyalarını çıkarmak için yeterli alan bulunmuyor.", FileServiceOperation.Extract, sourceArchiveFilePath, ex);
                    }
                    catch (IOException ex)
                    {
                        _logger.LogError(ex, "Disk I/O hatası (dosya kilitli): {File}", sourceArchiveFilePath);
                        throw new FileServiceException(
                            "Dosyalar başka bir işlem tarafından kullanıldığı için çıkarılamadı. Oyunun kapalı olduğundan emin olun.",
                            FileServiceOperation.Extract, sourceArchiveFilePath, ex);
                    }
                }
            });
        }

        public async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash)
        {
            _logger.LogInformation("Dosya doğrulaması başlatılıyor: {File}", filePath);

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Doğrulanacak dosya bulunamadı: {File}", filePath);
                return false;
            }

            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

                var hashBytes = await sha256.ComputeHashAsync(stream);
                var hash = BitConverter.ToString(hashBytes).Replace("-", "");

                bool isMatch = hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);

                if (isMatch)
                {
                    _logger.LogInformation("Dosya doğrulaması başarılı. Hash eşleşti: {File}", filePath);
                }
                else
                {
                    _logger.LogWarning("Dosya doğrulaması başarısız! Beklenen: {Expected}, Hesaplanan: {Actual}, Dosya: {File}", expectedHash, hash, filePath);
                }

                return isMatch;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dosya hash değeri hesaplanırken hata oluştu: {File}", filePath);
                return false;
            }
        }

        private void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                CopyFileWithRetry(file, Path.Combine(targetDir, file.Name));
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                CopyDirectoryRecursive(subDir.FullName, Path.Combine(targetDir, subDir.Name));
            }
        }

        private void CopyFileWithRetry(FileInfo file, string destinationPath, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    file.CopyTo(destinationPath, true);
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new FileServiceException(
                        "Erişim reddedildi.",
                        FileServiceOperation.Backup,
                        file.FullName, ex);
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning(ex, "Dosya kilitli, yeniden deneniyor ({Attempt}/{Max}): {File}",
                        attempt, maxRetries, file.FullName);
                    Thread.Sleep(attempt * 500);
                }
                catch (IOException ex)
                {
                    throw new FileServiceException(
                        "Dosya kopyalanamadı. Dosya başka bir işlem tarafından kullanılıyor olabilir.",
                        FileServiceOperation.Backup,
                        file.FullName, ex);
                }
            }
        }

        /// <summary>
        /// Diskte yeterli yer olup olmadığını kontrol eder. Eğer yeteri kadar alan boş değilse, hata fırlatır.
        /// </summary>
        /// <param name="requiredSize">İndirilecek tahmini dosya boyutu. Sıkıştırılmış olduğu için pay bırakılacaktır.</param>
        /// <param name="drivePath">Dosyanın çıkarılacağı / indirileceği konum.</param>
        public void IsEnoughSpaceOnDisk(long requiredSize, string drivePath)
        {
            // %30 pay eklendi.
            long estimatedSpaceNeeded = requiredSize + ((requiredSize * 30) / 100);

            string fullPath = Path.GetFullPath(drivePath);
            string driveRoot = Path.GetPathRoot(fullPath) ?? fullPath;

            DriveInfo driveInfo = new DriveInfo(driveRoot);

            if (driveInfo.AvailableFreeSpace < estimatedSpaceNeeded)
            {
                long missingBytes = estimatedSpaceNeeded - driveInfo.AvailableFreeSpace;
                double missingMb = Math.Round((double)missingBytes / (1024 * 1024), 2);

                throw new FileServiceException(
                    $"İşlem için diskte alan yetersiz. Lütfen ilgili diskte en az {missingMb} MB daha yer açın.",
                    FileServiceOperation.Extract, drivePath);
            }
        }
    }
}
