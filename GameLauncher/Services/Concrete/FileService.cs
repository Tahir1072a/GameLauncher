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

namespace GameLauncher.Services.Concrete
{
    public class FileService : IFileService
    {
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

                if (errors.Count > 0)
                    throw new AggregateException("Bazı dosyalar temizlenemedi.", errors);

                foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
                {
                    if (!protectedFolders.Contains(subDir.Name))
                    {
                        subDir.Delete(true);
                    }
                }
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
                try
                {
                    if (!File.Exists(sourceArchiveFilePath))
                        throw new FileNotFoundException("Kaynak arşiv dosyası bulunamadı.", sourceArchiveFilePath);

                    if (!Directory.Exists(extractPath))
                    {
                        Directory.CreateDirectory(extractPath);
                    }

                    ZipFile.ExtractToDirectory(sourceArchiveFilePath, extractPath, overwrite);
                    _logger.LogInformation("Arşiv başarıyla çıkarıldı: {Path}", extractPath);

                }
                catch (FileNotFoundException ex)
                {
                    _logger.LogError(ex, "Kaynak arşiv dosyası bulunamadı: {Path}", sourceArchiveFilePath);
                    throw new FileServiceException("Kaynak arşiv dosyası bulunamadı.", FileServiceOperation.Extract, sourceArchiveFilePath, ex);
                }
                catch (InvalidDataException ex)
                {
                    _logger.LogError(ex, "Bozuk veya geçersiz bir arşiv dosyası: {Path}", sourceArchiveFilePath);
                    throw new FileServiceException("Bozuk veya geçersiz bir arşiv dosyası.", FileServiceOperation.Extract, sourceArchiveFilePath, ex);
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "Disk I/O hatası: {File}", sourceArchiveFilePath);
                    throw new FileServiceException("Disk I/O hatası.", FileServiceOperation.Extract, sourceArchiveFilePath, ex);
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
                try
                {
                    file.CopyTo(Path.Combine(targetDir, file.Name), true);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new FileServiceException(
                        "Erişim reddedildi.",
                        FileServiceOperation.Backup,
                        file.FullName, ex);
                }
                catch (IOException ex)
                {
                    throw new FileServiceException(
                        "Dosya kopyalanamadı.",
                        FileServiceOperation.Backup,
                        file.FullName, ex);
                }
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                CopyDirectoryRecursive(subDir.FullName, Path.Combine(targetDir, subDir.Name));
            }
        }
    }
}
