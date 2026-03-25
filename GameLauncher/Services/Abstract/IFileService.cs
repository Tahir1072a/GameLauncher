using System;
using System.Collections.Generic;
using System.Text;

namespace GameLauncher.Services.Abstract
{
    public interface IFileService
    {
        Task<bool> VerifyFileHashAsync(string filePath, string expectedHash);
        Task CreateBackupAsync(string sourcePath, string backupPath);
        Task CleanDirectoryAsync(string directoryPath);
        Task ExtractZipAsync(string sourceArchiveFilePath, string extractPath, bool overwrite = true);
        Task DeleteFileAsync(string filePath);
    }
}
