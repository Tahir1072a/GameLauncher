using System;

namespace GameLauncher.Entites
{
    public class ServerManifest
    {
        public AppVersionDto launcher { get; set; } = new AppVersionDto { Version = new Version("0.0.0") };
        public AppVersionDto updater { get; set; } = new AppVersionDto { Version = new Version("0.0.0") };
        public AppVersionDto game { get; set; } = new AppVersionDto { Version = new Version("0.0.0") };
        public DateTime lastUpdated { get; set; }
        public bool initalRelease { get; set; }
    }
}
