using Microsoft.Win32.TaskScheduler;
using SystemCare.Helpers;
using SystemCare.Models;
using Task = System.Threading.Tasks.Task;

namespace SystemCare.Services;

public class MaintenanceResult
{
    public long BytesRemoved { get; init; }
    public long BytesFreed { get; init; }
    public int FilesRemoved { get; init; }
}

public interface IScheduledMaintenanceService
{
    /// <summary>Registers (or removes) the Windows scheduled task per the current settings.</summary>
    void Sync();
    bool TaskExists();
    /// <summary>Runs junk cleanup + RAM trim now. Shared by the tray menu and headless mode.</summary>
    Task<MaintenanceResult> RunMaintenanceNowAsync();
}

public class ScheduledMaintenanceService(
    IJunkScanService junkScan,
    IMemoryOptimizerService memoryOptimizer,
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
            td.Settings.DisallowStartIfOnBatteries = false;

            ts.RootFolder.RegisterTaskDefinition(TaskName, td);
            log.Info("Maintenance", $"Scheduled task registered ({settings.Current.MaintenanceFrequency}).");
        }
        catch (Exception ex)
        {
            // scheduling is best-effort; never crash the app over it
            log.Warn("Maintenance", $"Could not sync scheduled task: {ex.Message}");
        }
    }

    public async Task<MaintenanceResult> RunMaintenanceNowAsync()
    {
        var categoryIds = junkScan.Categories
            .Where(c => settings.Current.JunkCategoryToggles.GetValueOrDefault(c.Id, c.EnabledByDefault))
            .Select(c => c.Id)
            .ToList();

        var scan = await junkScan.ScanAsync(categoryIds, null, CancellationToken.None);
        var clean = await junkScan.CleanAsync(scan, categoryIds, null, CancellationToken.None);
        var ram = await memoryOptimizer.OptimizeAsync();

        settings.Current.LastScanUtc = DateTime.UtcNow;
        settings.Save();

        history.Record("Auto maintenance",
            $"Cleaned {ByteFormatter.Format(clean.BytesRemoved)} of junk · freed {ByteFormatter.Format(ram.BytesFreed)} of RAM",
            clean.BytesRemoved, clean.FilesRemoved, "Broom24");

        log.Info("Maintenance",
            $"Maintenance done — removed {ByteFormatter.Format(clean.BytesRemoved)} junk in {clean.FilesRemoved} file(s), freed {ByteFormatter.Format(ram.BytesFreed)} RAM.");

        return new MaintenanceResult
        {
            BytesRemoved = clean.BytesRemoved,
            FilesRemoved = clean.FilesRemoved,
            BytesFreed = ram.BytesFreed,
        };
    }
}
