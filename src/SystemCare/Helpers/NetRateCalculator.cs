using SystemCare.Models;

namespace SystemCare.Helpers;

/// <summary>Traffic intensity band for a process, used to pick the row's accent colour.</summary>
public enum NetUsageLevel { Low, Medium, High }

/// <summary>
/// Pure per-tick rate math for the Net Monitor page: turns two cumulative ETW byte snapshots into
/// per-process download/upload rates. Kept free of WPF types so it is unit-testable.
/// </summary>
public static class NetRateCalculator
{
    public const long MedThreshold = 256 * 1024;          // 256 KB/s
    public const long HighThreshold = 2L * 1024 * 1024;   // 2 MB/s

    public readonly record struct PidRates(int Pid, double Down, double Up, long TotalDown, long TotalUp)
    {
        public double Combined => Down + Up;
    }

    public readonly record struct RateResult(IReadOnlyList<PidRates> Rates, double TotalCombined, double MaxCombined);

    /// <summary>
    /// Computes per-PID rates between the previous and current cumulative snapshots. Counter resets
    /// (session restart, PID reuse) clamp to zero rather than reporting negative rates; a PID absent
    /// from <paramref name="prev"/> treats the snapshot totals as accumulated over this interval.
    /// </summary>
    public static RateResult Compute(IReadOnlyDictionary<int, ProcessNetSample> prev,
        IReadOnlyList<ProcessNetSample> snapshot, double intervalSeconds)
    {
        double interval = Math.Max(0.25, intervalSeconds);
        var rates = new List<PidRates>(snapshot.Count);
        double total = 0, max = 0;

        foreach (var s in snapshot)
        {
            long pSent = 0, pRecv = 0;
            if (prev.TryGetValue(s.Pid, out var p)) { pSent = p.SentBytes; pRecv = p.RecvBytes; }
            double down = Math.Max(0, (s.RecvBytes - pRecv) / interval);
            double up = Math.Max(0, (s.SentBytes - pSent) / interval);
            rates.Add(new PidRates(s.Pid, down, up, s.RecvBytes, s.SentBytes));

            double combined = down + up;
            total += combined;
            if (combined > max) max = combined;
        }

        return new RateResult(rates, total, max);
    }

    public static NetUsageLevel LevelFor(double combinedBytesPerSec) =>
        combinedBytesPerSec >= HighThreshold ? NetUsageLevel.High
        : combinedBytesPerSec >= MedThreshold ? NetUsageLevel.Medium
        : NetUsageLevel.Low;

    /// <summary>Share (0-100) of the total throughput, and bar fill (0-100) relative to the busiest process.</summary>
    public static (double Percent, double BarValue) Share(double combined, double totalCombined, double maxCombined) =>
        (totalCombined > 0 ? combined / totalCombined * 100 : 0,
         maxCombined > 0 ? combined / maxCombined * 100 : 0);
}
