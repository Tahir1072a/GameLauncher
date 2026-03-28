using GameLauncher.Services.Abstract;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GameLauncher.Services.Concrete
{
    public class FileService : IFileService
    {
        private readonly ILogger<FileService> _logger;

        public FileService(ILogger<FileService> logger)
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

                foreach (FileInfo file in dirInfo.GetFiles())
                {
                    var ext = file.Extension.ToLower();
                    bool isProtected = protectedExtensions.Contains(ext) || protectedFiles.Contains(file.Name);

                    if (!isProtected)
                    {
                        if (file.IsReadOnly) file.IsReadOnly = false;
                        file.Delete();
                    }
                }

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
                if (!Directory.Exists(sourcePath)) return;

                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                }

                Directory.CreateDirectory(backupPath);
                CopyDirectoryRecursive(sourcePath, backupPath);
            });
        }

        public Task DeleteFileAsync(string filePath)
        {
            return Task.Run(() =>
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            });
        }

        public Task ExtractZipAsync(string sourceArchiveFilePath, string extractPath, bool overwrite = true)
        {
            try
            {


            }
            catch (InvalidDataException ex)
            {

            }
            catch (IOException ex)
            {

            }

            return Task.Run(() =>
            {
                if (!Directory.Exists(extractPath))
                {
                    Directory.CreateDirectory(extractPath);
                }

                ZipFile.ExtractToDirectory(sourceArchiveFilePath, extractPath, overwrite);
            });
        }

        public async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash)
        {
            if (!File.Exists(filePath))
                return false;

            using var sha256 = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            var hashBytes = await sha256.ComputeHashAsync(stream);
            var hash = BitConverter.ToString(hashBytes).Replace("-", "");

            return hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        private void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                file.CopyTo(Path.Combine(targetDir, file.Name), true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                CopyDirectoryRecursive(subDir.FullName, Path.Combine(targetDir, subDir.Name));
            }
        }
    }
}
