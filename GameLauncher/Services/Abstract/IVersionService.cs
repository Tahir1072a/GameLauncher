using GameLauncher.Entites;
using System;
using System.Collections.Generic;
using System.Text;

namespace GameLauncher.Services.Abstract
{
    public interface IVersionService
    {
        Task<AppVersionDto?> GetLatestVersion(string appName, string platform);
        Task<AppVersionDto?> GetAlternativeVersionAsync(string alternativeUrl);
        Task<AppVersionDto?> GetLatestVersionWithFallbackAsync(string appName, string platform, string fallbackUrl);
    }
}
