using TaskSched = Microsoft.Win32.TaskScheduler;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IScheduledTaskManagerService
{
    /// <summary>Lists third-party (non-Microsoft) scheduled tasks. Never throws.</summary>
    Task<List<ScheduledTaskInfo>> ListAsync(CancellationToken ct);
    /// <summary>Enables or disables a task by its full path. Returns (ok, message).</summary>
    Task<(bool Ok, string Message)> SetEnabledAsync(string taskPath, bool enabled);
}

public class ScheduledTaskManagerService : IScheduledTaskManagerService
{
    private readonly ILogService _log;
    public ScheduledTaskManagerService(ILogService log) => _log = log;

    public Task<List<ScheduledTaskInfo>> ListAsync(CancellationToken ct) => Task.Run(() =>
    {
        var list = new List<ScheduledTaskInfo>();
        try
        {
            using var ts = new TaskSched.TaskService();
            foreach (var task in ts.AllTasks)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Skip Windows' own tasks — those aren't safe user-facing toggles.
                    if (task.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;

                    string folder = task.Path.Contains('\\')
                        ? task.Path[..task.Path.LastIndexOf('\\')] : "\\";
                    if (string.IsNullOrEmpty(folder)) folder = "\\";

                    string author = "";
                    try { author = task.Definition.RegistrationInfo.Author ?? ""; } catch (Exception) { }

                    list.Add(new ScheduledTaskInfo(
                        task.Path, task.Name, folder, task.Enabled, task.State.ToString(), author));
                }
                catch (Exception) { } // a single unreadable task shouldn't abort the list
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.Warn("ScheduledTasks", $"Enumeration failed: {ex.Message}"); }

        return list.OrderBy(t => t.Folder).ThenBy(t => t.Name).ToList();
    }, ct);

    public Task<(bool Ok, string Message)> SetEnabledAsync(string taskPath, bool enabled) => Task.Run(() =>
    {
        try
        {
            using var ts = new TaskSched.TaskService();
            var task = ts.GetTask(taskPath);
            if (task is null) return (false, "Task not found (it may have been removed).");
            task.Enabled = enabled;
            _log.Info("ScheduledTasks", $"{(enabled ? "Enabled" : "Disabled")} {taskPath}");
            return (true, $"{Path.GetFileName(taskPath)} {(enabled ? "enabled" : "disabled")}.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied — administrator rights are required.");
        }
        catch (Exception ex)
        {
            _log.Error("ScheduledTasks", "Toggle failed", ex);
            return (false, ex.Message);
        }
    });
}
