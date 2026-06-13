using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using SystemCare.Helpers;
using SystemCare.Models;
using Task = System.Threading.Tasks.Task;

namespace SystemCare.Services;

public interface IStartupManagerService
{
    Task<List<StartupEntry>> GetEntriesAsync(bool includeSystemTasks);
    /// <summary>Enables/disables without deleting, via the same StartupApproved mechanism Task Manager uses.</summary>
    bool SetEnabled(StartupEntry entry, bool enabled);
    bool DeleteEntry(StartupEntry entry);
}

public class StartupManagerService : IStartupManagerService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public Task<List<StartupEntry>> GetEntriesAsync(bool includeSystemTasks) => Task.Run(() =>
    {
        var entries = new List<StartupEntry>();
        CollectRunKey(entries, RegistryHive.CurrentUser, RunKey, StartupSource.HkcuRun);
        CollectRunKey(entries, RegistryHive.LocalMachine, RunKey, StartupSource.HklmRun);
        CollectRunKey(entries, RegistryHive.LocalMachine, RunKey32, StartupSource.HklmRun32);
        CollectStartupFolder(entries, Environment.SpecialFolder.Startup, StartupSource.UserStartupFolder);
        CollectStartupFolder(entries, Environment.SpecialFolder.CommonStartup, StartupSource.CommonStartupFolder);
        CollectScheduledTasks(entries, includeSystemTasks);
        return entries
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    // ---------- enumeration ----------

    private static void CollectRunKey(List<StartupEntry> entries, RegistryHive hive, string keyPath, StartupSource source)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            if (key is null) return;

            foreach (var valueName in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                string command = key.GetValue(valueName)?.ToString() ?? "";
                string? exe = CommandLineParser.ExtractExecutablePath(command);
                entries.Add(new StartupEntry
                {
                    Name = valueName,
                    Source = source,
                    Command = command,
                    ResolvedExePath = exe,
                    Publisher = GetPublisher(exe),
                    RawKey = valueName,
                    IsEnabled = ReadApprovedState(source, valueName),
                });
            }
        }
        catch (Exception)
        {
            // inaccessible hive view — skip
        }
    }

    private static void CollectStartupFolder(List<StartupEntry> entries, Environment.SpecialFolder folder, StartupSource source)
    {
        try
        {
            string path = Environment.GetFolderPath(folder);
            if (!Directory.Exists(path)) return;

            foreach (var lnk in Directory.EnumerateFiles(path, "*.lnk", SafeFileEnumerator.TopLevelOptions()))
            {
                string fileName = Path.GetFileName(lnk);
                string? target = ResolveShortcut(lnk);
                entries.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(lnk),
                    Source = source,
                    Command = target ?? lnk,
                    ResolvedExePath = target is not null && File.Exists(target) ? target : null,
                    Publisher = GetPublisher(target),
                    RawKey = lnk,
                    IsEnabled = ReadApprovedState(source, fileName),
                });
            }
        }
        catch (Exception) { }
    }

    private static void CollectScheduledTasks(List<StartupEntry> entries, bool includeSystemTasks)
    {
        try
        {
            using var service = new TaskService();
            foreach (var task in service.AllTasks)
            {
                try
                {
                    string taskPath = task.Path;
                    if (!includeSystemTasks && taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!task.Definition.Triggers.Any(t => t is LogonTrigger or BootTrigger))
                        continue;

                    string command = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault()?.Path ?? "";
                    string? exe = CommandLineParser.ExtractExecutablePath(command) ??
                                  (File.Exists(Environment.ExpandEnvironmentVariables(command.Trim('"')))
                                      ? Environment.ExpandEnvironmentVariables(command.Trim('"'))
                                      : null);
                    entries.Add(new StartupEntry
                    {
                        Name = task.Name,
                        Source = StartupSource.ScheduledTask,
                        Command = command,
                        ResolvedExePath = exe,
                        Publisher = GetPublisher(exe),
                        RawKey = taskPath,
                        IsEnabled = task.Enabled,
                    });
                }
                catch (Exception)
                {
                    // some system tasks throw on definition access — skip
                }
            }
        }
        catch (Exception) { }
    }

    private static string GetPublisher(string? exePath)
    {
        if (exePath is null || !File.Exists(exePath)) return "";
        try
        {
            return FileVersionInfo.GetVersionInfo(exePath).CompanyName ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string? ResolveShortcut(string lnkPath)
    {
        try
        {
            // Late-bound WScript.Shell avoids a COM interop package dependency.
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string? target = shortcut.TargetPath as string;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------- StartupApproved enable/disable ----------

    private static (RegistryHive Hive, string SubKey)? ApprovedLocation(StartupSource source) => source switch
    {
        StartupSource.HkcuRun => (RegistryHive.CurrentUser, ApprovedRoot + @"\Run"),
        StartupSource.HklmRun => (RegistryHive.LocalMachine, ApprovedRoot + @"\Run"),
        StartupSource.HklmRun32 => (RegistryHive.LocalMachine, ApprovedRoot + @"\Run32"),
        StartupSource.UserStartupFolder => (RegistryHive.CurrentUser, ApprovedRoot + @"\StartupFolder"),
        StartupSource.CommonStartupFolder => (RegistryHive.LocalMachine, ApprovedRoot + @"\StartupFolder"),
        _ => null,
    };

    private static bool ReadApprovedState(StartupSource source, string valueName)
    {
        var location = ApprovedLocation(source);
        if (location is null) return true;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(location.Value.Hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(location.Value.SubKey);
            // Missing value means the item was never toggled — enabled.
            if (key?.GetValue(valueName) is not byte[] data || data.Length == 0) return true;
            return (data[0] & 0x01) == 0; // 0x02/0x06 enabled, 0x03 disabled
        }
        catch (Exception)
        {
            return true;
        }
    }

    public bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.Source == StartupSource.ScheduledTask)
        {
            try
            {
                using var service = new TaskService();
                var task = service.GetTask(entry.RawKey);
                if (task is null) return false;
                task.Enabled = enabled;
                entry.IsEnabled = enabled;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        var location = ApprovedLocation(entry.Source);
        if (location is null) return false;
        try
        {
            string valueName = entry.Source is StartupSource.UserStartupFolder or StartupSource.CommonStartupFolder
                ? Path.GetFileName(entry.RawKey)
                : entry.RawKey;

            using var baseKey = RegistryKey.OpenBaseKey(location.Value.Hive, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(location.Value.SubKey, writable: true);

            byte[] data = new byte[12];
            if (enabled)
            {
                data[0] = 0x02;
            }
            else
            {
                // Task Manager writes 0x03 + FILETIME of when the item was disabled.
                data[0] = 0x03;
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            }
            key.SetValue(valueName, data, RegistryValueKind.Binary);
            entry.IsEnabled = enabled;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool DeleteEntry(StartupEntry entry)
    {
        try
        {
            switch (entry.Source)
            {
                case StartupSource.HkcuRun:
                case StartupSource.HklmRun:
                case StartupSource.HklmRun32:
                {
                    var hive = entry.Source == StartupSource.HkcuRun ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                    string keyPath = entry.Source == StartupSource.HklmRun32 ? RunKey32 : RunKey;
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var key = baseKey.OpenSubKey(keyPath, writable: true);
                    key?.DeleteValue(entry.RawKey, throwOnMissingValue: false);
                    return true;
                }
                case StartupSource.UserStartupFolder:
                case StartupSource.CommonStartupFolder:
                    File.Delete(entry.RawKey);
                    return true;
                case StartupSource.ScheduledTask:
                {
                    using var service = new TaskService();
                    service.RootFolder.DeleteTask(entry.RawKey.TrimStart('\\'), exceptionOnNotExists: false);
                    return true;
                }
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }
}
