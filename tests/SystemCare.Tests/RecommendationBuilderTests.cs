using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="RecommendationBuilder"/> maps probe results to ranked recommendations using the
/// real <see cref="HealthScoreService"/> penalty model, so every "up to +N points" claim matches
/// what a re-scan would show. Verifies per-rule thresholds, severity escalation, the unavailable
/// updates probe (-1), and ranking order.
/// </summary>
public class RecommendationBuilderTests
{
    private static readonly HealthScoreService Health = new();

    private static JunkScanResult Junk(long bytes, int files = 100)
    {
        var result = new JunkScanResult();
        result.Categories.Add(new JunkCategoryResult
        {
            Category = new JunkCategory { Id = "temp", Name = "Temp", Description = "" },
            TotalBytes = bytes,
            FileCount = files,
        });
        return result;
    }

    private static AutoCareProbeResults Probes(
        long junkBytes = 0, int startup = 0, double ram = 30, int security = 0, int updates = -1)
    {
        return new AutoCareProbeResults
        {
            Junk = Junk(junkBytes),
            EnabledStartupItems = startup,
            RamLoadPercent = ram,
            SecurityIssues = security,
            PendingSoftwareUpdates = updates,
            Health = Health.Compute(new HealthInputs
            {
                JunkBytes = junkBytes,
                EnabledStartupItems = startup,
                RamLoadPercent = ram,
                SecurityIssues = security,
            }),
        };
    }

    [Fact]
    public void HealthyPc_ProducesNoRecommendations()
    {
        Assert.Empty(RecommendationBuilder.Build(Probes()));
    }

    [Fact]
    public void TinyJunk_BelowOnePointPenalty_NotWorthRecommending()
    {
        // 10 MB → penalty ≈ 0.2 points; nagging about it would be noise.
        Assert.Empty(RecommendationBuilder.Build(Probes(junkBytes: 10L * 1024 * 1024)));
    }

    [Theory]
    [InlineData(1024L * 1024 * 1024, RecommendationSeverity.Suggested)]      // 1 GiB → 2 points
    [InlineData(4L * 1024 * 1024 * 1024, RecommendationSeverity.Important)]  // 4 GiB → 8 points
    public void JunkSeverity_EscalatesWithSize(long bytes, RecommendationSeverity expected)
    {
        var rec = Assert.Single(RecommendationBuilder.Build(Probes(junkBytes: bytes)));
        Assert.Equal("junk", rec.Id);
        Assert.Equal(expected, rec.Severity);
        Assert.Equal(RecommendationAction.CleanJunk, rec.Action);
        Assert.True(rec.IsDirectFix);
        Assert.Contains("reclaimable", rec.ImpactText);
    }

    [Theory]
    [InlineData(6, false)]  // six or fewer startup apps = no penalty
    [InlineData(7, true)]
    public void StartupRecommendation_TracksThePenaltyThreshold(int startupApps, bool expected)
    {
        var recs = RecommendationBuilder.Build(Probes(startup: startupApps));
        Assert.Equal(expected, recs.Any(r => r.Id == "startup"));
        if (expected) Assert.Equal("Startup", recs.Single(r => r.Id == "startup").NavigateTarget);
    }

    [Theory]
    [InlineData(70, false)]  // at/below 70% load = no penalty, no card
    [InlineData(75, true)]
    public void RamRecommendation_OnlyWhenPressureIsMeaningful(double ramLoad, bool expected)
    {
        Assert.Equal(expected, RecommendationBuilder.Build(Probes(ram: ramLoad)).Any(r => r.Id == "ram"));
    }

    [Fact]
    public void RamAbove85Percent_IsImportant()
    {
        var rec = Assert.Single(RecommendationBuilder.Build(Probes(ram: 90)), r => r.Id == "ram");
        Assert.Equal(RecommendationSeverity.Important, rec.Severity);
    }

    [Fact]
    public void SecurityIssues_AlwaysImportant_AndRankedFirst()
    {
        // Big junk (Important, 8 pts) + 1 security issue (Important, 10 pts). Both Important; among equals,
        // ranking is by recoverable points, so security (10) outranks junk (8).
        var recs = RecommendationBuilder.Build(Probes(junkBytes: 4L * 1024 * 1024 * 1024, security: 1));

        var sec = recs.Single(r => r.Id == "security");
        Assert.Equal(RecommendationSeverity.Important, sec.Severity);
        Assert.Equal("Security", sec.NavigateTarget);
        Assert.Equal("security", recs[0].Id); // 10 recoverable points beats 8
        Assert.Equal("junk", recs[1].Id);
    }

    [Theory]
    [InlineData(-1, false)] // probe unavailable — stay silent rather than claim "0 updates"
    [InlineData(0, false)]
    [InlineData(3, true)]
    public void UpdatesRecommendation_OnlyWhenProbeSucceededWithFindings(int pending, bool expected)
    {
        Assert.Equal(expected, RecommendationBuilder.Build(Probes(updates: pending)).Any(r => r.Id == "updates"));
    }

    [Theory]
    [InlineData(3, RecommendationSeverity.Info)]
    [InlineData(5, RecommendationSeverity.Suggested)]
    public void UpdatesSeverity_EscalatesWithCount(int pending, RecommendationSeverity expected)
    {
        var rec = Assert.Single(RecommendationBuilder.Build(Probes(updates: pending)), r => r.Id == "updates");
        Assert.Equal(expected, rec.Severity);
    }

    [Fact]
    public void Ranking_SeverityFirst_ThenRecoverablePoints()
    {
        // Suggested junk (2 pts) vs Important security (10 pts) vs Info updates.
        var recs = RecommendationBuilder.Build(Probes(junkBytes: 1024L * 1024 * 1024, security: 1, updates: 2));

        Assert.Equal(["security", "junk", "updates"], recs.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void PointsRecoverable_IsCappedAtAPerfectScore()
    {
        var probes = Probes(junkBytes: 4L * 1024 * 1024 * 1024, startup: 12, ram: 95, security: 3);
        var recs = RecommendationBuilder.Build(probes);

        double recoverable = RecommendationBuilder.PointsRecoverable(recs, probes.Health.Score);
        Assert.True(recoverable <= 100 - probes.Health.Score);
    }
}
