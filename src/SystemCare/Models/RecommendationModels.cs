namespace SystemCare.Models;

public enum RecommendationSeverity { Info, Suggested, Important }

/// <summary>What clicking a recommendation's button does: direct fixes call a service,
/// review actions navigate to the relevant tool.</summary>
public enum RecommendationAction { CleanJunk, TrimRam, ReviewStartup, ReviewSecurity, ReviewSoftwareUpdates }

/// <summary>One ranked, explained suggestion produced by the Auto Care analysis.</summary>
public class Recommendation
{
    /// <summary>Stable key, e.g. "junk" — one recommendation per probe area.</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>The probe numbers behind the suggestion, in plain language.</summary>
    public required string Explanation { get; init; }
    public RecommendationSeverity Severity { get; init; } = RecommendationSeverity.Suggested;
    /// <summary>e.g. "~1.2 GB reclaimable" or "+12 health points".</summary>
    public string ImpactText { get; init; } = "";
    /// <summary>Drives ranking; derived from the health-score penalty the fix would recover.</summary>
    public double HealthPointsRecoverable { get; init; }
    public RecommendationAction Action { get; init; }
    public string Icon { get; init; } = "Lightbulb24";
    /// <summary>MainWindow.NavigateTo key for review-type actions; null = direct in-page fix.</summary>
    public string? NavigateTarget { get; init; }
    public bool IsDirectFix => NavigateTarget is null;
}

/// <summary>Raw probe outputs feeding <c>RecommendationBuilder</c> (kept so Apply can reuse the scan).</summary>
public class AutoCareProbeResults
{
    public JunkScanResult? Junk { get; init; }
    public int EnabledStartupItems { get; init; }
    public double RamLoadPercent { get; init; }
    public int SecurityIssues { get; init; }
    /// <summary>-1 = probe unavailable (winget missing or the check failed).</summary>
    public int PendingSoftwareUpdates { get; init; } = -1;
    public HealthReport Health { get; init; } = new();
}

/// <summary>The full outcome of one Auto Care analysis.</summary>
public class AutoCareAnalysis
{
    public required AutoCareProbeResults Probes { get; init; }
    public required IReadOnlyList<Recommendation> Recommendations { get; init; }
    /// <summary>The junk category ids the probe scanned — Apply must clean exactly these.</summary>
    public IReadOnlyList<string> JunkCategoryIds { get; init; } = [];
}
