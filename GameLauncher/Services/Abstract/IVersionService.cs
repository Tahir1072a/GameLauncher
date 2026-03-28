using GameLauncher.Entites;
using System;
using System.Collections.Generic;
using System.Text;

namespace GameLauncher.Services.Abstract
{
    public interface IVersionService
    {
        Task<AppVersionDto?> GetLatestVersion(string appName, string platform);
    }
}
