namespace GameLauncher.Entites;

public class AppVersionDto
{
    // Sürümü olmayan bir sürüm olamaz, burada aslında nullable olmaması gerekir, bunu handle edeceğiz.
    public Version? Version { get; set; }
    public Version? MinVersion { get; set; }
    public bool IsMandatory { get; set; }
    public bool Critical { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ChecksumSha256 { get; set; }
    public long? Size { get; set; }
}
