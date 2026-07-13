using Microsoft.Win32.TaskScheduler;
using SystemCare.Helpers;
using SystemCare.Models;
using Task = System.Threading.Tasks.Task;

namespace SystemCare.Services;

/// <summary>Which steps a maintenance pass performs. <see cref="FromSettings"/> reads the user's
/// scheduled-maintenance profile; callers with fixed expectations pass an explicit profile.</summary>
public record MaintenanceProfile(bool CleanJunk, bool TrimRam, bool FlushDns, bool EmptyRecycleBin)
{
    /// <summary>The classic junk + RAM pass (pre-profile behaviour).</summary>
    public static readonly MaintenanceProfile JunkAndRam = new(true, true, false, false);

    public static MaintenanceProfile FromSettings(AppSettings s) =>
        new(s.MaintenanceCleanJunk, s.MaintenanceTrimRam, s.MaintenanceFlushDns, s.MaintenanceEmptyRecycleBin);
}

public class MaintenanceResult
{
    public bool JunkCleaned { get; init; }
    public long BytesRemoved { get; init; }
    public int FilesRemoved { get; init; }
    public bool RamTrimmed { get; init; }
    public long BytesFreed { get; init; }
    public bool DnsFlushed { get; init; }
    public bool RecycleBinEmptied { get; init; }
    public long RecycleBinBytes { get; init; }

    /// <summary>Human-readable one-liner of what actually happened, for balloons/history.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (JunkCleaned) parts.Add($"cleaned {ByteFormatter.Format(BytesRemoved)} of junk");
            if (RamTrimmed) parts.Add($"freed {ByteFormatter.Format(BytesFreed)} of RAM");
            if (DnsFlushed) parts.Add("flushed DNS");
            if (RecycleBinEmptied) parts.Add($"emptied Recycle Bin ({ByteFormatter.Format(RecycleBinBytes)})");
            if (parts.Count == 0) return "No maintenance steps ran.";
            string s = string.Join(" · ", parts);
            return char.ToUpperInvariant(s[0]) + s[1..];
        }
    }
}

public interface IScheduledMaintenanceService
{
    /// <summary>Registers (or removes) the Windows scheduled task per the current settings.</summary>
    void Sync();
    bool TaskExists();
    /// <summary>Runs the maintenance pass now. Shared by the tray menu and headless mode.
    /// <paramref name="profile"/> defaults to the user's scheduled-maintenance settings.</summary>
    Task<MaintenanceResult> RunMaintenanceNowAsync(MaintenanceProfile? profile = null);
}

public class ScheduledMaintenanceService(
    IJunkScanService junkScan,
    IMemoryOptimizerService memoryOptimizer,
    INetworkToolsService network,
    IRecycleBinService recycleBin,
    ISettingsService settings,
    IHistoryService history,
    ILogService log) : IScheduledMaintenanceService
{
    private const string TaskName = "SystemCare Auto Maintenance";

    public bool TaskExists()
    {
        try
        {
            using var ts = new TaskService();
            return ts.GetTask(TaskName) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Sync()
    {
        try
        {
            using var ts = new TaskService();
            if (!settings.Current.AutoMaintenanceEnabled)
            {
                ts.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
                return;
            }

            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SystemCare.exe");

            var td = ts.NewTask();
            td.RegistrationInfo.Description = "Runs SystemCare junk cleanup and memory optimization on a schedule.";
            td.Principal.RunLevel = TaskRunLevel.Highest;

            Trigger trigger = settings.Current.MaintenanceFrequency == "Daily"
                ? new DailyTrigger { DaysInterval = 1, StartBoundary = DateTime.Today.AddHours(12) }
                : new WeeklyTrigger { DaysOfWeek = DaysOfTheWeek.Sunday, StartBoundary = DateTime.Today.AddHours(12) };
            td.Triggers.Add(trigger);

            td.Actions.Add(new ExecAction(exe, "--run-maintenance", AppContext.BaseDirectory));
            td.Settings.StartWhenAvailable = true;

            // 2.14: optionally defer to a quiet moment — only while idle and on AC power.
            if (settings.Current.MaintenanceOnlyWhenIdle)
            {
                td.Settings.RunOnlyIfIdle = true;
                td.Settings.IdleSettings.StopOnIdleEnd = false;
                td.Settings.DisallowStartIfOnBatteries = true;
            }
            else
            {
                td.Settings.DisallowStartIfOnBatteries = false;
            }

            ts.RootFolder.RegisterTaskDefinition(TaskName, td);
            log.Info("Maintenance", $"Scheduled task registered ({settings.Current.MaintenanceFrequency}).");
        }
        catch (Exception ex)
        {
            // scheduling is best-effort; never crash the app over it
            log.Warn("Maintenance", $"Could not sync scheduled task: {ex.Message}");
        }
    }

    public async Task<MaintenanceResult> RunMaintenanceNowAsync(MaintenanceProfile? profile = null)
    {
        profile ??= MaintenanceProfile.FromSettings(settings.Current);

        // Each step is independently fault-isolated: an unreadable temp folder must not
        // cost the user the RAM trim (or any later step).
        bool junkCleaned = false, ramTrimmed = false, dnsFlushed = false, binEmptied = false;
        long junkBytes = 0, ramBytes = 0, binBytes = 0;
        int filesRemoved = 0;

        if (profile.CleanJunk)
        {
            try
            {
                var categoryIds = junkScan.Categories
                    .Where(c => settings.Current.JunkCategoryToggles.GetValueOrDefault(c.Id, c.EnabledByDefault))
                    .Select(c => c.Id)
                    .ToList();
                var scan = await junkScan.ScanAsync(categoryIds, null, CancellationToken.None);
                var clean = await junkScan.CleanAsync(scan, categoryIds, null, CancellationToken.None);
                junkBytes = clean.BytesRemoved;
                filesRemoved = clean.FilesRemoved;
                junkCleaned = true;
            }
            catch (Exception ex) { log.Warn("Maintenance", $"Junk cleanup step failed: {ex.Message}"); }
        }

        if (profile.TrimRam)
        {
            try
            {
                var ram = await memoryOptimizer.OptimizeAsync();
                ramBytes = ram.BytesFreed;
                ramTrimmed = true;
            }
            catch (Exception ex) { log.Warn("Maintenance", $"RAM trim step failed: {ex.Message}"); }
        }

        if (profile.FlushDns)
        {
            try
            {
                network.FlushDns();
                dnsFlushed = true;
            }
            catch (Exception ex) { log.Warn("Maintenance", $"DNS flush step failed: {ex.Message}"); }
        }

        if (profile.EmptyRecycleBin)
        {
            try
            {
                var (bytes, items) = recycleBin.Query();
                if (items > 0)
                {
                    recycleBin.Empty();
                    binBytes = bytes;
                    binEmptied = true;
                }
            }
            catch (Exception ex) { log.Warn("Maintenance", $"Recycle Bin step failed: {ex.Message}"); }
        }

        settings.Current.LastScanUtc = DateTime.UtcNow;
        settings.Save();

        var result = new MaintenanceResult
        {
            JunkCleaned = junkCleaned,
            BytesRemoved = junkBytes,
            FilesRemoved = filesRemoved,
            RamTrimmed = ramTrimmed,
            BytesFreed = ramBytes,
            DnsFlushed = dnsFlushed,
            RecycleBinEmptied = binEmptied,
            RecycleBinBytes = binBytes,
        };

        history.Record("Auto maintenance", result.Summary, junkBytes + binBytes, filesRemoved, "Broom24");
        log.Info("Maintenance", $"Maintenance done — {result.Summary}");
        return result;
    }
}
