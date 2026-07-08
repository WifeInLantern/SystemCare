using SystemCare.Models;

namespace SystemCare.Services;

public interface IHealthScoreService
{
    HealthReport Compute(HealthInputs inputs);
}

public class HealthScoreService : IHealthScoreService
{
    // 5 GiB of junk ⇒ the full (small) 10-point junk penalty. Junk is housekeeping, not a health emergency.
    private const double JunkFullPenaltyBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>
    /// Weighted 0–100 penalty model (v2). The weights reflect real impact on a PC's health rather than mere
    /// tidiness: <b>security posture</b> (30) and <b>low system-drive free space</b> (25) dominate because they
    /// genuinely degrade safety, performance and stability; sustained <b>memory pressure</b> (20) matters only
    /// past 70% load (Windows uses free RAM for caching); <b>startup load</b> (15) and <b>junk</b> (10) are
    /// gentle so ordinary housekeeping can't tank an otherwise-healthy machine. Max total penalty = 100.
    /// </summary>
    public HealthReport Compute(HealthInputs inputs)
    {
        // Security — the single biggest real risk. Up to 30.
        double securityPenalty = Math.Min(30, 10.0 * inputs.SecurityIssues);

        // Low free space on the system drive. No penalty at/above 20% free; ramps to the full 25 at 0%.
        // A 0% reading is treated as "unknown" (no penalty) so a missing value never tanks the score.
        double p = inputs.SystemDriveFreePercent;
        double diskPenalty = (p > 0 && p < 20) ? Math.Min(25, (20 - p) * 1.25) : 0;

        // Sustained memory pressure — nothing below 70% load; ramps to the full 20 by ~95%.
        double ramPenalty = inputs.RamLoadPercent <= 70 ? 0 : Math.Min(20, (inputs.RamLoadPercent - 70) * 0.8);

        // Startup load — free allowance of 6 apps, then a gentle 2.5 points each, capped at 15.
        double startupPenalty = Math.Min(15, 2.5 * Math.Max(0, inputs.EnabledStartupItems - 6));

        // Junk — capped low so even a lot of it barely moves the score.
        double junkPenalty = Math.Min(10, 10.0 * inputs.JunkBytes / JunkFullPenaltyBytes);

        int score = (int)Math.Clamp(
            100 - securityPenalty - diskPenalty - ramPenalty - startupPenalty - junkPenalty, 0, 100);

        return new HealthReport
        {
            Score = score,
            Band = score switch
            {
                >= 90 => HealthBand.Excellent,
                >= 70 => HealthBand.Good,
                >= 40 => HealthBand.NeedsAttention,
                _ => HealthBand.Poor,
            },
            JunkPenalty = junkPenalty,
            StartupPenalty = startupPenalty,
            RamPenalty = ramPenalty,
            SecurityPenalty = securityPenalty,
            DiskPenalty = diskPenalty,
        };
    }
}
