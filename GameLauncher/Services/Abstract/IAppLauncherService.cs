namespace GameLauncher.Services.Abstract
{
    public interface IAppLauncherService
    {
        AppLaunchResult LaunchApplication(string gameDirectory, string mainExeName);
        void MarkRepairCompleted(string gameDirectory);
    }

    public class AppLaunchResult
    {
        public bool IsSuccess { get; set; }
        public bool RequiresAutoRepair { get; set; }
        public bool IsUnrepairableSystemError { get; set; }
        public string? ErrorTitle { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
