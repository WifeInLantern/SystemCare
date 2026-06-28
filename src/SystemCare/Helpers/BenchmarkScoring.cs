namespace SystemCare.Helpers;

/// <summary>
/// Pure scoring math for the PC benchmark: turns raw measured metrics (CPU MOps/s, RAM GB/s, disk MB/s)
/// into 0-100 sub-scores against reference baselines, then a weighted overall index and headline points.
/// Kept dependency-free so it can be unit-tested; the actual measuring lives in <c>BenchmarkService</c>.
///
/// The reference constants below map a typical mid-range PC to a sub-score of ~50; a machine twice as fast
/// reaches 100. They are deliberately easy to retune without touching any logic.
/// </summary>
public static class BenchmarkScoring
{
    // Measured metric that corresponds to a sub-score of 50 (calibrated against real hardware).
    public const double CpuRefMOps = 1300.0;   // multi-thread xorshift throughput
    public const double RamRefGBps = 35.0;     // sequential copy bandwidth (counts read + write)
    public const double DiskRefMBps = 1200.0;  // sequential write-through throughput

    // Overall-index weights (sum to 1.0).
    public const double CpuWeight = 0.5;
    public const double RamWeight = 0.2;
    public const double DiskWeight = 0.3;

    /// <summary>Normalize a measured value to 0-100 where <paramref name="reference"/> maps to 50.</summary>
    public static double SubScore(double measured, double reference)
    {
        if (reference <= 0 || measured <= 0) return 0;
        return Math.Clamp(measured / reference * 50.0, 0, 100);
    }

    public static double CpuScore(double mops) => SubScore(mops, CpuRefMOps);
    public static double RamScore(double gbps) => SubScore(gbps, RamRefGBps);
    public static double DiskScore(double mbps) => SubScore(mbps, DiskRefMBps);

    /// <summary>Weighted 0-100 index from the three sub-scores.</summary>
    public static double Overall(double cpuScore, double ramScore, double diskScore) =>
        Math.Clamp(cpuScore * CpuWeight + ramScore * RamWeight + diskScore * DiskWeight, 0, 100);

    /// <summary>Headline "Night City" points (the overall 0-100 index scaled to a big number).</summary>
    public static int Points(double overallIndex) => (int)Math.Round(Math.Clamp(overallIndex, 0, 100) * 100);
}
