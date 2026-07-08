using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IAutoCareService
{
    /// <summary>Runs the read-only probes (junk scan, startup count, security checkup, RAM load,
    /// pending winget updates) and builds ranked recommendations. Changes nothing.</summary>
    Task<AutoCareAnalysis> AnalyzeAsync(IProgress<string>? progress, CancellationToken ct);
}

/// <summary>
/// Orchestrates the Auto Care analysis: the same probes the Dashboard scan uses, plus a winget
/// update check, feeding <see cref="RecommendationBuilder"/>. The winget probe is the slow one
/// (5–20 s) and is fault-tolerant — a missing/failing winget reports -1 (unavailable), never fails
/// the analysis.
/// </summary>
public class AutoCareService(
    IJunkScanService junkScan,
    IStartupManagerService startup,
    ISecurityCheckService security,
    ISoftwareUpdateService softwareUpdates,
    ISystemInfoService systemInfo,
    IHealthScoreService healthScore,
    ISettingsService settings,
    ILogService log) : IAutoCareService
{
    public async Task<AutoCareAnalysis> AnalyzeAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var categoryIds = junkScan.Categories
            .Where(c => settings.Current.JunkCategoryToggles.GetValueOrDefault(c.Id, c.EnabledByDefault))
            .Select(c => c.Id)
            .ToList();

        progress?.Report("Scanning junk, startup apps, and security…");
        var junkTask = junkScan.ScanAsync(categoryIds, null, ct);
        var startupTask = startup.GetEntriesAsync(includeSystemTasks: false);
        var securityTask = security.GetChecksAsync();
        var updatesTask = ProbeSoftwareUpdatesAsync(ct); // slowest — started first, awaited last

        await Task.WhenAll(junkTask, startupTask, securityTask);
        progress?.Report("Checking for app updates (winget)…");
        int pendingUpdates = await updatesTask;
        ct.ThrowIfCancellationRequested();

        var junk = junkTask.Result;
        int enabledStartup = startupTask.Result.Count(e => e.IsEnabled);
        int securityIssues = securityTask.Result.Count(c =>
            c.Status is SecurityStatus.Warning or SecurityStatus.Bad);
        var snapshot = systemInfo.GetSnapshot(includeDrives: true);

        var health = healthScore.Compute(new HealthInputs
        {
            JunkBytes = junk.TotalBytes,
            EnabledStartupItems = enabledStartup,
            RamLoadPercent = snapshot.RamLoadPercent,
            SecurityIssues = securityIssues,
            SystemDriveFreePercent = DriveMetrics.SystemDriveFreePercent(snapshot.Drives),
        });

        var probes = new AutoCareProbeResults
        {
            Junk = junk,
            EnabledStartupItems = enabledStartup,
            RamLoadPercent = snapshot.RamLoadPercent,
            SecurityIssues = securityIssues,
            PendingSoftwareUpdates = pendingUpdates,
            Health = health,
        };

        return new AutoCareAnalysis
        {
            Probes = probes,
            Recommendations = RecommendationBuilder.Build(probes),
            JunkCategoryIds = categoryIds,
        };
    }

    private async Task<int> ProbeSoftwareUpdatesAsync(CancellationToken ct)
    {
        try
        {
            if (!await softwareUpdates.IsAvailableAsync(ct)) return -1;
            var upgrades = await softwareUpdates.GetUpgradesAsync(ct);
            var excluded = settings.Current.SoftwareUpdateExclusions;
            return upgrades.Count(u => !excluded.Contains(u.Id, StringComparer.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Warn("AutoCare", $"Software-update probe failed: {ex.Message}");
            return -1;
        }
    }
}
