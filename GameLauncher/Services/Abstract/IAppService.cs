using GameLauncher.Entites;
using System;
using System.Threading.Tasks;

namespace GameLauncher.Services.Abstract
{
    /// <summary>
    /// This is the service that manages all the functions related to the main game,
    /// such as downloading, installing and verifying the game files.
    /// </summary>
    public interface IAppService
    {
        /// <summary>
        /// Orchestrates the process of downloading and installing the main game app.
        /// Performs hash validation up to 3 times on failure.
        /// Handles backing up old files and cleaning the directory before extraction.
        /// </summary>
        Task<bool> DownloadAndInstallUpdateAsync(
            AppVersionDto gameVersion, 
            ServerManifest localManifest, 
            string gameDirectory, 
            IProgress<DownloadState> progressReporter);

        /// <summary>
        /// Determines if the current game version requires an update or a critical rollback.
        /// </summary>
        bool RequiresUpdate(AppVersionDto? newVersion, AppVersionDto currentVersion);
        
        /// <summary>
        /// Determines if the update is mandatory.
        /// </summary>
        bool IsMandatoryUpdate(AppVersionDto? newVersion, AppVersionDto currentVersion);
    }
}
