using System.IO;
using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IAutorunGuardService
{
    /// <summary>
    /// Compares the current startup entries against the snapshot from the previous run and, when
    /// Autorun Guard is enabled, raises a tray notification for anything that added itself since.
    /// Always refreshes the snapshot. Best-effort: never throws.
    /// </summary>
    Task CheckAsync();
}

/// <summary>
/// Autorun Guard (2.14): detects programs that silently register themselves to run at startup.
/// A snapshot of startup entries (Run keys, startup folders, scheduled tasks) is persisted next to
/// settings.json; on each interactive app start the current set is diffed against it. New entries
/// produce a tray balloon pointing the user at the Startup Manager, where the existing reversible
/// StartupApproved toggle can disable them. Detection only — this service never modifies entries.
/// </summary>
public sealed class AutorunGuardService(
    IStartupManagerService startup,
    ISettingsService settings,
    ITrayIconService tray,
    IHistoryService history,
    ILogService log) : IAutorunGuardService
{
    private sealed record AutorunSnapshotEntry(string Key, string Name);

    private static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SystemCare", "autorun-snapshot.json");

    /// <summary>Stable identity for an entry across runs: where it lives + its raw store key.</summary>
    private static string IdentityOf(StartupEntry e) => $"{e.Source}|{e.RawKey}";

    public async Task CheckAsync()
    {
        try
        {
            // System tasks are excluded: they churn with Windows servicing and would cause noise.
            var entries = await startup.GetEntriesAsync(includeSystemTasks: false);
            var current = entries
                .Select(e => new AutorunSnapshotEntry(IdentityOf(e), e.Name))
                .DistinctBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<AutorunSnapshotEntry>? previous = LoadSnapshot();
            SaveSnapshot(current);

            // First run (no snapshot yet): establish the baseline silently.
            if (previous is null) return;
            if (!settings.Current.AutorunGuardEnabled) return;

            var known = previous.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = current.Where(c => !known.Contains(c.Key)).ToList();
            if (added.Count == 0) return;

            string names = string.Join(", ", added.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(3));
            if (added.Count > 3) names += $" (+{added.Count - 3} more)";

            string title = added.Count == 1 ? "New startup entry detected" : $"{added.Count} new startup entries detected";
            tray.ShowBalloon(title, $"{names} — review in Startup Manager to keep or disable (reversible).");
            history.Record("Autorun Guard", $"{title}: {names}", itemCount: added.Count, icon: "Rocket24");
            log.Info("AutorunGuard", $"{title}: {names}");
        }
        catch (Exception ex)
        {
            // Guard is a convenience watchdog; a failed check must never disturb startup.
            log.Error("AutorunGuard", "Startup-entry check failed", ex);
        }
    }

    private static List<AutorunSnapshotEntry>? LoadSnapshot()
    {
        try
        {
            if (!File.Exists(SnapshotPath)) return null;
            return JsonSerializer.Deserialize<List<AutorunSnapshotEntry>>(File.ReadAllText(SnapshotPath));
        }
        catch (Exception)
        {
            return null; // corrupt snapshot => re-baseline
        }
    }

    private static void SaveSnapshot(List<AutorunSnapshotEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(entries));
        }
        catch (Exception)
        {
            // best-effort
        }
    }
}
