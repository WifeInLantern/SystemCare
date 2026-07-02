using SystemCare.Models;

namespace SystemCare.Helpers;

/// <summary>
/// Pure rules turning Auto Care probe results into ranked recommendations. Thresholds are anchored
/// to <c>HealthScoreService</c>'s penalty model so "up to +N points" claims match what a re-scan
/// would actually show: each recommendation's weight IS the penalty its fix would recover.
/// </summary>
internal static class RecommendationBuilder
{
    public static List<Recommendation> Build(AutoCareProbeResults r)
    {
        var list = new List<Recommendation>();
        var health = r.Health;

        if (r.Junk is not null && health.JunkPenalty >= 1)
        {
            list.Add(new Recommendation
            {
                Id = "junk",
                Title = "Clean up junk files",
                Explanation = $"{ByteFormatter.Format(r.Junk.TotalBytes)} of junk across {r.Junk.TotalFiles:N0} files is weighing on your health score.",
                Severity = health.JunkPenalty >= 20 ? RecommendationSeverity.Important : RecommendationSeverity.Suggested,
                ImpactText = $"~{ByteFormatter.Format(r.Junk.TotalBytes)} reclaimable · +{health.JunkPenalty:0} points",
                HealthPointsRecoverable = health.JunkPenalty,
                Action = RecommendationAction.CleanJunk,
                Icon = "Broom24",
            });
        }

        if (health.StartupPenalty > 0)
        {
            list.Add(new Recommendation
            {
                Id = "startup",
                Title = "Trim your startup apps",
                Explanation = $"{r.EnabledStartupItems} apps launch with Windows — each one slows boot and stays resident. Four or fewer is healthy.",
                Severity = health.StartupPenalty >= 15 ? RecommendationSeverity.Important : RecommendationSeverity.Suggested,
                ImpactText = $"+{health.StartupPenalty:0} points · faster boot",
                HealthPointsRecoverable = health.StartupPenalty,
                Action = RecommendationAction.ReviewStartup,
                Icon = "Rocket24",
                NavigateTarget = "Startup",
            });
        }

        if (health.RamPenalty >= 7) // RAM load above ~60%
        {
            list.Add(new Recommendation
            {
                Id = "ram",
                Title = "Free up memory",
                Explanation = $"RAM is {r.RamLoadPercent:0}% full. Trimming working sets gives active apps room to breathe.",
                Severity = r.RamLoadPercent >= 85 ? RecommendationSeverity.Important : RecommendationSeverity.Suggested,
                ImpactText = $"+{health.RamPenalty:0} points",
                HealthPointsRecoverable = health.RamPenalty,
                Action = RecommendationAction.TrimRam,
                Icon = "Ram20",
            });
        }

        if (r.SecurityIssues > 0)
        {
            list.Add(new Recommendation
            {
                Id = "security",
                Title = r.SecurityIssues == 1 ? "Fix a security issue" : $"Fix {r.SecurityIssues} security issues",
                Explanation = $"The security checkup flagged {r.SecurityIssues} issue(s) — Defender, firewall, UAC, Remote Desktop, or update recency.",
                Severity = RecommendationSeverity.Important, // security always leads
                ImpactText = $"+{health.SecurityPenalty:0} points · safer PC",
                HealthPointsRecoverable = health.SecurityPenalty,
                Action = RecommendationAction.ReviewSecurity,
                Icon = "ShieldCheckmark24",
                NavigateTarget = "Security",
            });
        }

        if (r.PendingSoftwareUpdates > 0)
        {
            list.Add(new Recommendation
            {
                Id = "updates",
                Title = r.PendingSoftwareUpdates == 1 ? "1 app has an update" : $"{r.PendingSoftwareUpdates} apps have updates",
                Explanation = "Outdated apps miss security patches and fixes. Review and update them via winget.",
                Severity = r.PendingSoftwareUpdates >= 5 ? RecommendationSeverity.Suggested : RecommendationSeverity.Info,
                ImpactText = "patched & current apps",
                HealthPointsRecoverable = 0,
                Action = RecommendationAction.ReviewSoftwareUpdates,
                Icon = "ArrowSync24",
                NavigateTarget = "SoftwareUpdater",
            });
        }

        return list
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.HealthPointsRecoverable)
            .ToList();
    }

    /// <summary>Total health points all direct+review fixes could recover, capped so the headline
    /// never promises more than a perfect score.</summary>
    public static double PointsRecoverable(IEnumerable<Recommendation> recs, int currentScore) =>
        Math.Min(100 - currentScore, recs.Sum(x => x.HealthPointsRecoverable));
}
