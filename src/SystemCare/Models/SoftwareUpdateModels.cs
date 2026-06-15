namespace SystemCare.Models;

/// <summary>An installed app with a newer version available, as reported by <c>winget upgrade</c>.</summary>
public class SoftwareUpdate
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string AvailableVersion { get; init; } = "";
    public string Source { get; init; } = "";

    public string VersionChangeText =>
        string.IsNullOrWhiteSpace(CurrentVersion) ? $"→ {AvailableVersion}" : $"{CurrentVersion} → {AvailableVersion}";
}

public class SoftwareUpdateProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string Name { get; init; } = "";
    public double Percent { get; init; }
}

public class SoftwareUpdateResult
{
    public int Updated { get; init; }
    public int Failed { get; init; }
    public string Message { get; init; } = "";
}
