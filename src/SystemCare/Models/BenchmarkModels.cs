namespace SystemCare.Models;

/// <summary>The outcome of one benchmark run: raw per-test metrics, their 0-100 sub-scores, and the
/// combined index + headline "Night City" points.</summary>
public class BenchmarkResult
{
    public double CpuMOps { get; init; }
    public double RamGBps { get; init; }
    public double DiskMBps { get; init; }

    public double CpuScore { get; init; }
    public double RamScore { get; init; }
    public double DiskScore { get; init; }

    public double OverallIndex { get; init; }
    public int Points { get; init; }

    public string CpuText => $"{CpuMOps:N0} MOps/s";
    public string RamText => $"{RamGBps:0.0} GB/s";
    public string DiskText => $"{DiskMBps:N0} MB/s";
}

/// <summary>Progress update raised while a benchmark runs (phase label + overall percent).</summary>
public class BenchmarkProgress
{
    public string Phase { get; init; } = "";
    public int Percent { get; init; }
}

/// <summary>A persisted past run used to draw the score trend.</summary>
public class BenchmarkRun
{
    public DateTime TimestampUtc { get; init; }
    // Raw measured metrics (kept so the score can be recalibrated and for a future "show details" view).
    public double CpuMOps { get; init; }
    public double RamGBps { get; init; }
    public double DiskMBps { get; init; }
    public double CpuScore { get; init; }
    public double RamScore { get; init; }
    public double DiskScore { get; init; }
    public double OverallIndex { get; init; }
    public int Points { get; init; }
}
