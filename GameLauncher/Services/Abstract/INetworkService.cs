using GameLauncher.Services.Concrete;
using System;
using System.Collections.Generic;
using System.Text;

namespace GameLauncher.Services.Abstract
{
    public interface INetworkService
    {
        Task<bool> CheckInternetConnectionAsync(string url);
        Task DownloadFilesAsync(string url, string destinationPath, IProgress<DownloadState> progress = null);
    }
}
