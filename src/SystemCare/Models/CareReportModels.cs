namespace SystemCare.Models;

/// <summary>One health-score sample (at most one per calendar day), persisted for the trend chart.</summary>
public class HealthSnapshot
{
    public DateTime TimestampUtc { get; set; }
    public int Score { get; set; }
}

/// <summary>Everything the exported HTML care report is built from, gathered up front so the
/// HTML builder stays a pure, testable function.</summary>
public class CareReportData
{
    public string AppVersion { get; init; } = "";
    public DateTime GeneratedLocal { get; init; } = DateTime.Now;
    public int? HealthScore { get; init; }
    public IReadOnlyList<HardwareSpec> Specs { get; init; } = [];
    public IReadOnlyList<HistoryEntry> History { get; init; } = [];
    public IReadOnlyList<HealthSnapshot> HealthTrend { get; init; } = [];
    public IReadOnlyList<BenchmarkRun> BenchmarkRuns { get; init; } = [];
}
