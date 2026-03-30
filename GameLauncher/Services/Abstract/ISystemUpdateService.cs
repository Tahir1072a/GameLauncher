using GameLauncher.Entites;
using System;
using System.Threading.Tasks;

namespace GameLauncher.Services.Abstract
{
    public interface ISystemUpdateService
    {
        /// <summary>
        /// Checks if the Updater or Launcher components need updating based on the provided API version info.
        /// If an update is needed, it optionally downloads, handles fallbacks/downgrades if Critical=true,
        /// applies the update, and might terminate the application for restart.
        /// </summary>
        Task<bool> CheckAndUpdateSystemComponentsAsync(
            AppVersionDto? launcherVersion, 
            AppVersionDto? updaterVersion, 
            ServerManifest localManifest, 
            string appDirectory,
            string updaterDirectory,
            IProgress<DownloadState> progressReporter);
    }
}
