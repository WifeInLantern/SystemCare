using SystemCare.Models;

namespace SystemCare.Helpers;

/// <summary>
/// Pure stability scoring for the Reliability Center: each recorded problem subtracts a category-weighted
/// penalty from 100. Dependency-free so it can be unit-tested; the Event-Log reading lives in
/// <c>ReliabilityService</c>.
/// </summary>
public static class ReliabilityScore
{
    /// <summary>Points deducted per occurrence — severe, system-wide failures cost the most.</summary>
    public static int Penalty(ReliabilityCategory category) => category switch
    {
        ReliabilityCategory.BlueScreen => 25,
        ReliabilityCategory.UnexpectedShutdown => 20,
        ReliabilityCategory.DiskError => 12,
        ReliabilityCategory.Crash => 8,
        ReliabilityCategory.ServiceFailure => 4,
        ReliabilityCategory.AppHang => 3,
        _ => 5,
    };

    public static int Score(IEnumerable<ReliabilityEvent> events)
    {
        int score = 100;
        foreach (var e in events) score -= Penalty(e.Category);
        return Math.Clamp(score, 0, 100);
    }

    public static string Tier(int score) => score switch
    {
        >= 90 => "Stable",
        >= 70 => "Mostly stable",
        >= 40 => "Unstable",
        _ => "Critical",
    };
}
