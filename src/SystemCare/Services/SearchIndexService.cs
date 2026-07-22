using Microsoft.Win32;
using SystemCare.Helpers;

namespace SystemCare.Services;

public sealed class SearchIndexStatus
{
    public long IndexBytes { get; init; }
    public string IndexPath { get; init; } = "";
    public bool ServiceRunning { get; init; }
}

public interface ISearchIndexService
{
    Task<SearchIndexStatus> GetStatusAsync();
    /// <summary>Triggers the documented Windows Search rebuild: SetupCompletedSuccessfully=0 +
    /// WSearch restart. Windows recreates the index in the background (search is degraded meanwhile).</summary>
    Task<(bool Ok, string Message)> RebuildAsync();
    /// <summary>Opens the classic Indexing Options control panel.</summary>
    void OpenIndexingOptions();
}

/// <summary>
/// Windows Search index health (2.16): the index database (Windows.edb / Windows.db) is a classic
/// hidden disk-space and disk-thrash culprit — it can silently grow to tens of GB or corrupt.
/// This service reports its size and offers the documented rebuild path (registry flag + service
/// restart), which Windows itself uses; nothing is deleted by hand.
/// </summary>
public sealed class SearchIndexService(IHistoryService history, ILogService log) : ISearchIndexService
{
    private const string ServiceName = "WSearch";

    public Task<SearchIndexStatus> GetStatusAsync() => Task.Run(() =>
    {
        var (path, size) = FindIndexFile();
        return new SearchIndexStatus
        {
            IndexBytes = size,
            IndexPath = path,
            ServiceRunning = IsServiceRunning(),
        };
    });

    public Task<(bool Ok, string Message)> RebuildAsync() => Task.Run(async () =>
    {
        try
        {
            // The same flag Indexing Options' own "Rebuild" button sets.
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Search"))
                key.SetValue("SetupCompletedSuccessfully", 0, RegistryValueKind.DWord);

            // "service is not started" from the stop is fine — we only need it down before the start.
            // ProcessRunner bounds every run by default (2.19.4); WSearch gets a longer explicit
            // window because a large index can take a while to release.
            var window = TimeSpan.FromSeconds(90);
            _ = await ProcessRunner.RunAsync("net.exe", $"stop {ServiceName}", window, CancellationToken.None);
            var start = await ProcessRunner.RunAsync("net.exe", $"start {ServiceName}", window, CancellationToken.None);
            if (start.ExitCode != 0)
                return (false, $"Couldn't restart Windows Search: {start.Output.Trim()}");

            history.Record("Search index", "Triggered a Windows Search index rebuild", icon: "DocumentSearch24");
            log.Info("SearchIndex", "Rebuild triggered (flag + WSearch restart).");
            return (true, "Rebuild started — Windows recreates the index in the background. Search results may be incomplete for a while.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Administrator rights are required to rebuild the search index.");
        }
        catch (OperationCanceledException)
        {
            return (false, "Windows Search took too long to restart — the rebuild flag is set, so a reboot will complete it.");
        }
        catch (Exception ex)
        {
            log.Error("SearchIndex", "Rebuild failed", ex);
            return (false, ex.Message);
        }
    });

    public void OpenIndexingOptions()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "srchadmin.dll")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            log.Warn("SearchIndex", $"Couldn't open Indexing Options: {ex.Message}");
        }
    }

    private static (string Path, long Size) FindIndexFile()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Search", "Data", "Applications", "Windows");
        foreach (var name in new[] { "Windows.edb", "Windows.db" })
        {
            try
            {
                var info = new FileInfo(Path.Combine(dataDir, name));
                if (info.Exists) return (info.FullName, info.Length);
            }
            catch (Exception) { }
        }
        return ("", 0);
    }

    private static bool IsServiceRunning()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(ServiceName);
            return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
