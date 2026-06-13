namespace SystemCare.Models;

public enum DiskHealthStatus { Healthy, Warning, Unhealthy, Unknown }

public class PhysicalDiskHealth
{
    public required string Name { get; init; }
    public long SizeBytes { get; init; }
    public string MediaType { get; init; } = "Disk";
    public DiskHealthStatus Health { get; init; } = DiskHealthStatus.Unknown;
    public string HealthText => Health switch
    {
        DiskHealthStatus.Healthy => "Healthy",
        DiskHealthStatus.Warning => "Warning",
        DiskHealthStatus.Unhealthy => "Failing",
        _ => "Unknown",
    };
}
