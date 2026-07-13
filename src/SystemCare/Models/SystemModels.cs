namespace SystemCare.Models;

public class SystemSnapshot
{
    /// <summary>0–100, or null until two CPU samples exist.</summary>
    public double? CpuPercent { get; init; }
    public ulong RamTotalBytes { get; init; }
    public ulong RamAvailableBytes { get; init; }
    public ulong RamUsedBytes => RamTotalBytes - RamAvailableBytes;
    public double RamLoadPercent { get; init; }
    public IReadOnlyList<DriveStat> Drives { get; init; } = [];
    /// <summary>Network throughput since the previous snapshot, in bytes/second (0 on the first sample).</summary>
    public double NetRecvBytesPerSec { get; init; }
    public double NetSentBytesPerSec { get; init; }
}

public class DriveStat
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes > 0 ? UsedBytes * 100.0 / TotalBytes : 0;
    /// <summary>2.14 Storage Forecast: "~5 weeks until full at the current rate", or null when
    /// there is no meaningful downward trend. Set by the dashboard, not by SystemInfoService.</summary>
    public string? Forecast { get; set; }
}

public class HealthInputs
{
    public long JunkBytes { get; init; }
    public int EnabledStartupItems { get; init; }
    public double RamLoadPercent { get; init; }
    /// <summary>Number of security checks reporting a warning or worse.</summary>
    public int SecurityIssues { get; init; }
    /// <summary>Free space on the system drive as a percentage (0–100). 0 = unknown ⇒ no penalty.</summary>
    public double SystemDriveFreePercent { get; init; }
}

public enum HealthBand { Excellent, Good, NeedsAttention, Poor }

public class HealthReport
{
    public int Score { get; init; }
    public HealthBand Band { get; init; }
    public double JunkPenalty { get; init; }
    public double StartupPenalty { get; init; }
    public double RamPenalty { get; init; }
    public double SecurityPenalty { get; init; }
    public double DiskPenalty { get; init; }
}
