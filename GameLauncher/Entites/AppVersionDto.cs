namespace GameLauncher.Entites;

public class AppVersionDto
{
    public Version Version { get; set; }
    public Version? MinVersion { get; set; }
    public bool IsMandatory { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ChecksumSha256 { get; set; }
    public long? Size { get; set; }
}
