using System;
using System.Collections.Generic;
using System.Text;

namespace GameLauncher.Services.Abstract
{
    public interface IFileService
    {
        Task<bool> VerifyFileHashAsync(string filePath, string expectedHash);
        Task CleanDirectoryAsync(string directoryPath);
        Task ExtractZipAsync(string sourceArchiveFilePath, string extractPath, bool overwrite = true);
        Task DeleteFileAsync(string filePath);
        void IsEnoughSpaceOnDisk(long requiredSize, string drivePath);
        Task MoveExtractedFilesAsync(string sourceDir, string targetDir);
    }
}
