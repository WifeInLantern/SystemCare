namespace SystemCare.Models;

public enum DiskHealthStatus { Healthy, Warning, Unhealthy, Unknown }

/// <summary>Alert severity, ordered so higher values sort first.</summary>
public enum DiskUrgency { Info, Caution, Warning, Critical }

public class PhysicalDiskHealth
{
    public required string Name { get; init; }
    public long SizeBytes { get; init; }
    public string MediaType { get; init; } = "Disk";
    public DiskHealthStatus Health { get; init; } = DiskHealthStatus.Unknown;

    // SMART reliability counters — null when the drive/controller doesn't expose them.
    public int? WearPercent { get; init; }
    public double? TemperatureC { get; set; } // settable: filled from the sensor backend after the WMI sweep
    public long? PowerOnHours { get; init; }
    public long? ReadErrors { get; init; }
    public long? WriteErrors { get; init; }
    public long? ReallocatedSectors { get; init; }

    // Filled in by IDiskHealthScoreService. Score < 0 = not yet scored.
    public int Score { get; set; } = -1;
    public string ScoreBand { get; set; } = "";

    public string HealthText => Health switch
    {
        DiskHealthStatus.Healthy => "Healthy",
        DiskHealthStatus.Warning => "Warning",
        DiskHealthStatus.Unhealthy => "Failing",
        _ => "Unknown",
    };
}

/// <summary>One predictive/health alert with an optional recommended action.</summary>
public class DiskAlert
{
    public required DiskUrgency Urgency { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    public string ActionLabel { get; init; } = "";
    /// <summary>A MainWindow nav id (Cleanup/Duplicates/Disk), "__maintain", "__restorepoint", or null.</summary>
    public string? ActionTarget { get; init; }
}
