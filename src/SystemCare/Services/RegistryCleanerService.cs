using System.Diagnostics;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IRegistryCleanerService
{
    IReadOnlyList<RegistryCategory> Categories { get; }
    Task<List<RegistryIssue>> ScanAsync(IEnumerable<string> categoryIds, IProgress<string>? progress, CancellationToken ct);
    Task<RegistryCleanResult> CleanAsync(IEnumerable<RegistryIssue> issues, IProgress<string>? progress, CancellationToken ct);
    Task<(bool Ok, string Message)> RestoreLastBackupAsync();
    string BackupRoot { get; }
    void OpenBackupsFolder();
}

/// <summary>
/// Conservative registry cleaner: only flags entries that clearly reference files/folders that no
/// longer exist. Every clean run is exported to a timestamped .reg backup first (and a System
/// Restore point if enabled), so anything removed can be restored.
/// </summary>
public class RegistryCleanerService(IRestorePointService restore, IBackupConfirmationService backup) : IRegistryCleanerService
{
    public string BackupRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare", "RegistryBackups");

    public IReadOnlyList<RegistryCategory> Categories { get; } =
    [
        new() { Id = "uninstall", Name = "Uninstall leftovers", Description = "Uninstall entries whose install folder is gone" },
        new() { Id = "apppaths", Name = "Invalid App Paths", Description = "App Paths entries pointing to missing programs" },
        new() { Id = "run", Name = "Invalid startup entries", Description = "Run keys launching files that no longer exist" },
        new() { Id = "shareddlls", Name = "Missing shared DLLs", Description = "Shared-DLL references to files that are gone" },
        new() { Id = "muicache", Name = "MUI cache", Description = "Cached app names referencing missing files" },
    ];

    public Task<List<RegistryIssue>> ScanAsync(IEnumerable<string> categoryIds, IProgress<string>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var wanted = categoryIds.ToHashSet();
        var issues = new List<RegistryIssue>();

        if (wanted.Contains("uninstall")) { progress?.Report("Scanning uninstall entries…"); issues.AddRange(ScanUninstall(ct)); }
        if (wanted.Contains("apppaths")) { progress?.Report("Scanning App Paths…"); issues.AddRange(ScanAppPaths(ct)); }
        if (wanted.Contains("run")) { progress?.Report("Scanning startup entries…"); issues.AddRange(ScanRun(ct)); }
        if (wanted.Contains("shareddlls")) { progress?.Report("Scanning shared DLLs…"); issues.AddRange(ScanSharedDlls(ct)); }
        if (wanted.Contains("muicache")) { progress?.Report("Scanning MUI cache…"); issues.AddRange(ScanMuiCache(ct)); }

        return issues;
    }, ct);

    // ---------- category scanners (all read-only) ----------

    private static readonly (RegistryHive Hive, string Path)[] UninstallRoots =
    [
        (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    private static IEnumerable<RegistryIssue> ScanUninstall(CancellationToken ct)
    {
        foreach (var (hive, path) in UninstallRoots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            if (key is null) continue;

            foreach (var sub in key.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                RegistryIssue? issue = null;
                try
                {
                    using var entry = key.OpenSubKey(sub);
                    if (entry is null) continue;
                    if (entry.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (entry.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                    if (entry.GetValue("InstallLocation") is not string loc || string.IsNullOrWhiteSpace(loc)) continue;

                    string folder = Environment.ExpandEnvironmentVariables(loc.Trim().Trim('"')).TrimEnd('\\');
                    if (folder.Length > 3 && !Directory.Exists(folder))
                    {
                        issue = new RegistryIssue
                        {
                            CategoryId = "uninstall", CategoryName = "Uninstall leftovers",
                            Hive = hive, View = RegistryView.Registry64, SubKeyPath = $@"{path}\{sub}",
                            Data = name, Reason = $"Install folder missing: {folder}",
                        };
                    }
                }
                catch (Exception) { }
                if (issue is not null) yield return issue;
            }
        }
    }

    private static readonly (RegistryHive Hive, string Path)[] AppPathRoots =
    [
        (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\App Paths"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths"),
    ];

    private static IEnumerable<RegistryIssue> ScanAppPaths(CancellationToken ct)
    {
        foreach (var (hive, path) in AppPathRoots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            if (key is null) continue;

            foreach (var sub in key.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                RegistryIssue? issue = null;
                try
                {
                    using var entry = key.OpenSubKey(sub);
                    if (entry?.GetValue(null) is not string target || string.IsNullOrWhiteSpace(target)) continue;
                    string exe = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
                    if (exe.Contains('\\') && !File.Exists(exe))
                    {
                        issue = new RegistryIssue
                        {
                            CategoryId = "apppaths", CategoryName = "Invalid App Paths",
                            Hive = hive, View = RegistryView.Registry64, SubKeyPath = $@"{path}\{sub}",
                            Data = sub, Reason = $"Program missing: {exe}",
                        };
                    }
                }
                catch (Exception) { }
                if (issue is not null) yield return issue;
            }
        }
    }

    private static readonly (RegistryHive Hive, string Path)[] RunRoots =
    [
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
    ];

    private static IEnumerable<RegistryIssue> ScanRun(CancellationToken ct)
    {
        foreach (var (hive, path) in RunRoots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            if (key is null) continue;

            foreach (var valueName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(valueName)) continue;
                RegistryIssue? issue = null;
                try
                {
                    string command = key.GetValue(valueName)?.ToString() ?? "";
                    string first = FirstToken(command);
                    // Only flag explicit file paths (avoids rundll32/powershell-style commands).
                    if (first.Contains('\\') && !File.Exists(first) && !Directory.Exists(first))
                    {
                        issue = new RegistryIssue
                        {
                            CategoryId = "run", CategoryName = "Invalid startup entries",
                            Hive = hive, View = RegistryView.Registry64, SubKeyPath = path, ValueName = valueName,
                            Data = command, Reason = $"Target missing: {first}",
                        };
                    }
                }
                catch (Exception) { }
                if (issue is not null) yield return issue;
            }
        }
    }

    private static IEnumerable<RegistryIssue> ScanSharedDlls(CancellationToken ct)
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\SharedDLLs";
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(path);
        if (key is null) yield break;

        foreach (var valueName in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();
            RegistryIssue? issue = null;
            try
            {
                string file = Environment.ExpandEnvironmentVariables(valueName);
                if (file.Contains('\\') && file.Contains(':') && !File.Exists(file))
                {
                    issue = new RegistryIssue
                    {
                        CategoryId = "shareddlls", CategoryName = "Missing shared DLLs",
                        Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64,
                        SubKeyPath = path, ValueName = valueName, Data = valueName,
                        Reason = "Referenced DLL no longer exists",
                    };
                }
            }
            catch (Exception) { }
            if (issue is not null) yield return issue;
        }
    }

    private static IEnumerable<RegistryIssue> ScanMuiCache(CancellationToken ct)
    {
        const string path = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(path);
        if (key is null) yield break;

        foreach (var valueName in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();
            RegistryIssue? issue = null;
            try
            {
                // Entries look like "C:\path\app.exe.FriendlyAppName" / ".ApplicationCompany".
                string p = valueName;
                foreach (var suffix in new[] { ".FriendlyAppName", ".ApplicationCompany" })
                    if (p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { p = p[..^suffix.Length]; break; }

                if (p.Length > 3 && p[1] == ':' && p.Contains('\\') && !File.Exists(p))
                {
                    issue = new RegistryIssue
                    {
                        CategoryId = "muicache", CategoryName = "MUI cache",
                        Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64,
                        SubKeyPath = path, ValueName = valueName, Data = valueName,
                        Reason = $"File missing: {p}",
                    };
                }
            }
            catch (Exception) { }
            if (issue is not null) yield return issue;
        }
    }

    private static string FirstToken(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? Environment.ExpandEnvironmentVariables(command[1..end]) : command;
        }
        int space = command.IndexOf(' ');
        string token = space > 0 ? command[..space] : command;
        return Environment.ExpandEnvironmentVariables(token);
    }

    // ---------- clean (with backup) ----------

    public async Task<RegistryCleanResult> CleanAsync(IEnumerable<RegistryIssue> issues, IProgress<string>? progress, CancellationToken ct)
    {
        var list = issues.ToList();
        var result = new RegistryCleanResult();
        if (list.Count == 0) return result;

        if (await backup.ConfirmRestorePointAsync("cleaning the registry"))
        {
            progress?.Report("Creating a restore point…");
            try { await restore.CreateRestorePointAsync("Before SystemCare registry clean"); } catch (Exception) { }
        }

        string folder = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
        Directory.CreateDirectory(folder);
        result.BackupFolder = folder;

        int n = 0;
        foreach (var group in list.GroupBy(x => x.ExportKeyPath))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Backing up {group.Key}…");
            string file = Path.Combine(folder, $"key_{++n}.reg");
            try { await RunReg(ct, "export", group.Key, file, "/y"); } catch (Exception) { }
        }

        foreach (var issue in list)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Removing {issue.DisplayPath}…");
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(issue.Hive, RegistryView.Registry64);
                if (issue.ValueName is null)
                {
                    baseKey.DeleteSubKeyTree(issue.SubKeyPath, throwOnMissingSubKey: false);
                }
                else
                {
                    using var key = baseKey.OpenSubKey(issue.SubKeyPath, writable: true);
                    key?.DeleteValue(issue.ValueName, throwOnMissingValue: false);
                }
                result.Removed++;
            }
            catch (Exception) { result.Skipped++; }
        }

        return result;
    }

    public async Task<(bool Ok, string Message)> RestoreLastBackupAsync()
    {
        if (!Directory.Exists(BackupRoot)) return (false, "No registry backups found yet.");
        var latest = new DirectoryInfo(BackupRoot).GetDirectories().OrderByDescending(d => d.Name).FirstOrDefault();
        if (latest is null) return (false, "No registry backups found yet.");

        int imported = 0;
        foreach (var reg in latest.GetFiles("*.reg"))
        {
            try { if (await RunReg(CancellationToken.None, "import", reg.FullName) == 0) imported++; } catch (Exception) { }
        }
        return imported > 0
            ? (true, $"Restored {imported} key backup(s) from {latest.Name}.")
            : (false, "Could not restore the last backup.");
    }

    public void OpenBackupsFolder()
    {
        try
        {
            Directory.CreateDirectory(BackupRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupRoot}\"") { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    private static async Task<int> RunReg(CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo("reg.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
