using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IInstalledAppsService
{
    Task<List<InstalledApp>> GetInstalledAppsAsync();
    /// <summary>Runs the app's uninstaller and waits for it to finish. Returns true if it launched.</summary>
    Task<bool> UninstallAsync(InstalledApp app);
}

public class InstalledAppsService : IInstalledAppsService
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallPath32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<List<InstalledApp>> GetInstalledAppsAsync() => Task.Run(() =>
    {
        var byName = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        Collect(RegistryHive.LocalMachine, UninstallPath, byName);
        Collect(RegistryHive.LocalMachine, UninstallPath32, byName);
        Collect(RegistryHive.CurrentUser, UninstallPath, byName);

        return byName.Values
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    private static void Collect(RegistryHive hive, string path, Dictionary<string, InstalledApp> byName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            if (key is null) return;

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    if (sub is null) continue;

                    string? name = sub.GetValue("DisplayName") as string;
                    string? uninstall = sub.GetValue("UninstallString") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uninstall)) continue;

                    // Skip OS components and update entries.
                    if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (sub.GetValue("ParentKeyName") is not null) continue;
                    if (sub.GetValue("ReleaseType") is string rt &&
                        (rt.Contains("Update", StringComparison.OrdinalIgnoreCase) || rt.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (byName.ContainsKey(name)) continue;

                    byName[name] = new InstalledApp
                    {
                        Name = name,
                        Version = sub.GetValue("DisplayVersion") as string ?? "",
                        Publisher = sub.GetValue("Publisher") as string ?? "",
                        InstallDate = ParseInstallDate(sub.GetValue("InstallDate") as string),
                        SizeBytes = sub.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0,
                        InstallLocation = (sub.GetValue("InstallLocation") as string)?.Trim('"'),
                        IconPath = (sub.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim('"'),
                        UninstallString = uninstall,
                        QuietUninstallString = sub.GetValue("QuietUninstallString") as string,
                    };
                }
                catch (Exception)
                {
                    // unreadable entry — skip
                }
            }
        }
        catch (Exception)
        {
            // hive not accessible — skip
        }
    }

    private static DateTime? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    public Task<bool> UninstallAsync(InstalledApp app) => Task.Run(() =>
    {
        string command = !string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? app.QuietUninstallString!
            : app.UninstallString;
        try
        {
            // Run through cmd so MsiExec /X{GUID}, unins000.exe and quoted/argumented
            // strings all launch correctly.
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });
}
