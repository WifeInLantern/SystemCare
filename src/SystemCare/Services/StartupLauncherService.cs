using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace SystemCare.Services;

public interface IStartupLauncherService
{
    /// <summary>True if the "start with Windows" logon task is currently registered.</summary>
    bool IsEnabled();
    /// <summary>Creates or removes the logon task to match the persisted setting; refreshes the exe path.</summary>
    void Sync();
}

/// <summary>
/// Runs SystemCare at sign-in via a Task Scheduler logon task rather than an <c>HKCU\…\Run</c> entry.
/// The app requires administrator rights, so a Run-key launch would pop a UAC prompt at every logon;
/// a scheduled task with <see cref="TaskRunLevel.Highest"/> starts it elevated and silently. The task
/// launches the app with <c>--minimized</c> so it goes straight to the tray.
/// </summary>
public class StartupLauncherService(ISettingsService settings, ILogService log) : IStartupLauncherService
{
    private const string TaskName = "SystemCare Autostart";

    public bool IsEnabled()
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
            if (!settings.Current.StartWithWindows)
            {
                ts.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
                return;
            }

            // Re-resolve the exe path each time so the task keeps working after an in-place upgrade.
            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SystemCare.exe");

            using var identity = WindowsIdentity.GetCurrent();
            string user = identity.Name;

            var td = ts.NewTask();
            td.RegistrationInfo.Description = "Starts SystemCare minimized to the tray when you sign in.";
            // Highest privileges => launches the elevated app at logon with no UAC prompt.
            td.Principal.RunLevel = TaskRunLevel.Highest;
            td.Triggers.Add(new LogonTrigger { UserId = user });
            td.Actions.Add(new ExecAction(exe, "--minimized", AppContext.BaseDirectory));

            td.Settings.StartWhenAvailable = true;
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.ExecutionTimeLimit = TimeSpan.Zero; // the app stays running; don't let the task time it out

            ts.RootFolder.RegisterTaskDefinition(TaskName, td);
            log.Info("Autostart", $"Logon task registered for {user}.");
        }
        catch (Exception ex)
        {
            // best-effort; never crash the app over startup registration
            log.Warn("Autostart", $"Could not sync the autostart task: {ex.Message}");
        }
    }
}
