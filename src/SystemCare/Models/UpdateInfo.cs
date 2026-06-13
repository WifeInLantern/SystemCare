namespace SystemCare.Models;

public class UpdateInfo
{
    public required string Version { get; init; }
    public string DownloadUrl { get; init; } = "";
    public string Notes { get; init; } = "";
}
