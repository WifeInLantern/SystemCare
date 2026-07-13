using System.Management;
using Microsoft.Win32;
using SystemCare.Helpers;

namespace SystemCare.Services;

public sealed class PageFileInfo
{
    public string Path { get; init; } = "";
    public long AllocatedBytes { get; init; }
    public long InUseBytes { get; init; }
}

public sealed class PowerStorageStatus
{
    public bool HibernationEnabled { get; init; }
    /// <summary>hiberfil.sys size; 0 when hibernation is off or the file is unreadable.</summary>
    public long HiberfilBytes { get; init; }
    public IReadOnlyList<PageFileInfo> PageFiles { get; init; } = [];
    public long PageFilesTotalBytes { get; init; }
}

public interface IPowerStorageAdvisorService
{
    Task<PowerStorageStatus> GetStatusAsync();
    /// <summary>powercfg /hibernate off — reclaims hiberfil.sys. Reversible via Enable. Disables Fast Startup too.</summary>
    Task<(bool Ok, string Message)> DisableHibernationAsync();
    /// <summary>powercfg /hibernate on — restores hibernation + Fast Startup.</summary>
    Task<(bool Ok, string Message)> EnableHibernationAsync();
    /// <summary>powercfg /h /type reduced — keeps Fast Startup but shrinks hiberfil.sys (~40% of RAM).</summary>
    Task<(bool Ok, string Message)> SetReducedHibernationAsync();
}

/// <summary>
/// Hibernation &amp; Pagefile advisor (2.16): shows what hiberfil.sys and pagefile.sys actually
/// cost and offers the reversible <c>powercfg</c> hibernation actions. The pagefile itself is
/// deliberately read-only in v1 — resizing it wrongly can destabilize a system, so we inform
/// and let Windows manage it.
/// </summary>
public sealed class PowerStorageAdvisorService(IHistoryService history, ILogService log) : IPowerStorageAdvisorService
{
    public Task<PowerStorageStatus> GetStatusAsync() => Task.Run(() =>
    {
        bool enabled = ReadHibernationEnabled();
        long hiberfil = ReadFileSizeSafe(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "hiberfil.sys"));
        var pageFiles = ReadPageFiles();
        return new PowerStorageStatus
        {
            HibernationEnabled = enabled,
            HiberfilBytes = hiberfil,
            PageFiles = pageFiles,
            PageFilesTotalBytes = pageFiles.Sum(p => p.AllocatedBytes),
        };
    });

    public Task<(bool Ok, string Message)> DisableHibernationAsync() =>
        RunPowerCfgAsync("/hibernate off", "Hibernation disabled — hiberfil.sys has been removed. (Fast Startup is off too; re-enable any time.)", "Disabled hibernation");

    public Task<(bool Ok, string Message)> EnableHibernationAsync() =>
        RunPowerCfgAsync("/hibernate on", "Hibernation re-enabled (full size) — Fast Startup is available again.", "Enabled hibernation");

    public Task<(bool Ok, string Message)> SetReducedHibernationAsync() =>
        RunPowerCfgAsync("/hibernate /type reduced", "Hibernation set to reduced — Fast Startup keeps working with a ~40%-of-RAM hiberfil.sys.", "Set reduced hibernation");

    private async Task<(bool Ok, string Message)> RunPowerCfgAsync(string args, string successMessage, string historySummary)
    {
        try
        {
            var (exit, output) = await ProcessRunner.RunAsync("powercfg.exe", args);
            if (exit != 0)
            {
                string detail = string.IsNullOrWhiteSpace(output) ? $"powercfg exited with code {exit}." : output.Trim();
                log.Warn("PowerStorage", $"powercfg {args} failed: {detail}");
                return (false, detail);
            }
            history.Record("Hibernation", historySummary, icon: "Sleep24");
            log.Info("PowerStorage", $"powercfg {args} OK");
            return (true, successMessage);
        }
        catch (Exception ex)
        {
            log.Error("PowerStorage", $"powercfg {args} threw", ex);
            return (false, ex.Message);
        }
    }

    private static bool ReadHibernationEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return key?.GetValue("HibernateEnabled") is int v && v != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static long ReadFileSizeSafe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static List<PageFileInfo> ReadPageFiles()
    {
        var result = new List<PageFileInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            using var rows = searcher.Get();
            foreach (var row in rows)
            {
                result.Add(new PageFileInfo
                {
                    Path = row["Name"] as string ?? "pagefile.sys",
                    AllocatedBytes = ToMbBytes(row["AllocatedBaseSize"]),
                    InUseBytes = ToMbBytes(row["CurrentUsage"]),
                });
            }
        }
        catch (Exception)
        {
            // WMI unavailable — advisor degrades to hibernation-only.
        }
        return result;

        static long ToMbBytes(object? value) =>
            value is null ? 0 : Convert.ToInt64(value) * 1024L * 1024L;
    }
}
