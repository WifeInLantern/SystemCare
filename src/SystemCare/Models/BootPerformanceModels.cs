namespace SystemCare.Models;

public class BootPerformanceReport
{
    public DateTime LastBootUtc { get; init; }
    public string UptimeText { get; init; } = "";
    /// <summary>Total boot time in ms from the Diagnostics-Performance log; 0 if that log is unavailable.</summary>
    public int BootDurationMs { get; init; }
    public List<StartupImpact> Apps { get; init; } = [];

    public bool HasBootData => BootDurationMs > 0;
    public string LastBootText => LastBootUtc == default ? "unknown" : LastBootUtc.ToLocalTime().ToString("g");
    public string BootDurationText => BootDurationMs > 0 ? $"{BootDurationMs / 1000.0:0.0} s" : "not recorded";
}

public class StartupImpact
{
    public string Name { get; init; } = "";
    public int DurationMs { get; init; }
    public bool IsService { get; init; }

    public string DurationText => $"{DurationMs / 1000.0:0.0} s";
    public string Kind => IsService ? "Service" : "App";
    public string Level => DurationMs >= 5000 ? "High" : DurationMs >= 2000 ? "Medium" : "Low";
}
