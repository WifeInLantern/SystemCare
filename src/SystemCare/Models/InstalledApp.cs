namespace SystemCare.Models;

public class InstalledApp
{
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public long SizeBytes { get; init; }
    public string? InstallLocation { get; init; }
    public string? IconPath { get; init; }
    public required string UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
}
