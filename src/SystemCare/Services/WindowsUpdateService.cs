using System.Diagnostics;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IWindowsUpdateService
{
    /// <summary>Searches Windows Update for pending non-driver updates. Read-only; never throws. Caches results for install.</summary>
    Task<List<WindowsUpdateItem>> CheckAsync(CancellationToken ct);
    Task<WindowsUpdateInstallResult> InstallAsync(IEnumerable<WindowsUpdateItem> updates, IProgress<WindowsUpdateProgress>? progress, CancellationToken ct);
    Task<List<WindowsUpdateHistoryItem>> GetHistoryAsync();
    (bool Ok, string Message) Pause(int days);
    (bool Ok, string Message) Resume();
    void OpenWindowsUpdate();
}

/// <summary>
/// Windows (non-driver) updates via the Windows Update Agent (WUA) COM API, late-bound through
/// <c>dynamic</c> so no interop package is needed — the same approach as <c>DriverUpdateService</c>.
/// Pause/resume writes the documented Windows Update "UX" settings (best-effort; exact keys vary by build).
/// </summary>
public class WindowsUpdateService : IWindowsUpdateService
{
    private const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private static readonly string[] PauseValues =
    [
        "PauseUpdatesExpiryTime", "PauseUpdatesStartTime",
        "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
    ];

    // COM update objects from the last search, addressed by WindowsUpdateItem.Index.
    private readonly List<object> _lastUpdates = [];

    public Task<List<WindowsUpdateItem>> CheckAsync(CancellationToken ct) => Task.Run(() =>
    {
        var results = new List<WindowsUpdateItem>();
        _lastUpdates.Clear();

        try
        {
            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return results;

            dynamic session = Activator.CreateInstance(sessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();

            ct.ThrowIfCancellationRequested();
            dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");
            dynamic updates = searchResult.Updates;

            foreach (dynamic u in updates)
            {
                ct.ThrowIfCancellationRequested();

                long size = 0;
                try { size = Convert.ToInt64(u.MaxDownloadSize); } catch (Exception) { }
                bool mandatory = false;
                try { mandatory = (bool)u.IsMandatory; } catch (Exception) { }
                string kb = "";
                try { foreach (dynamic id in u.KBArticleIDs) { kb = "KB" + id; break; } } catch (Exception) { }
                string category = "";
                try { foreach (dynamic c in u.Categories) { category = (string)c.Name; break; } } catch (Exception) { }
                string title = "";
                try { title = ((string?)u.Title)?.Trim() ?? ""; } catch (Exception) { }
                if (title.Length == 0) title = kb.Length > 0 ? kb : "Windows update";

                // Index must stay in lockstep with _lastUpdates so install can resolve the COM object.
                int idx = _lastUpdates.Count;
                _lastUpdates.Add(u);
                results.Add(new WindowsUpdateItem
                {
                    Title = title,
                    Kb = kb,
                    SizeBytes = size,
                    IsMandatory = mandatory,
                    Category = category,
                    Index = idx,
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // WU service unavailable / offline / managed by policy — treat as "no updates".
        }

        return results;
    });

    public Task<WindowsUpdateInstallResult> InstallAsync(IEnumerable<WindowsUpdateItem> updates,
        IProgress<WindowsUpdateProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var picks = updates.Where(u => u.Index >= 0 && u.Index < _lastUpdates.Count).ToList();
        if (picks.Count == 0)
            return new WindowsUpdateInstallResult { Message = "No updates selected." };

        int installed = 0, failed = 0;
        bool reboot = false;

        try
        {
            Type sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                ?? throw new InvalidOperationException("Windows Update Agent is unavailable.");
            dynamic session = Activator.CreateInstance(sessionType)!;
            Type collType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!;

            int i = 0;
            foreach (var pick in picks)
            {
                ct.ThrowIfCancellationRequested();
                i++;
                progress?.Report(new WindowsUpdateProgress
                {
                    Current = i, Total = picks.Count, Title = pick.Title, Percent = (i - 1) * 100.0 / picks.Count,
                });

                try
                {
                    dynamic u = _lastUpdates[pick.Index];
                    try { if (!(bool)u.EulaAccepted) u.AcceptEula(); } catch (Exception) { }

                    dynamic coll = Activator.CreateInstance(collType)!;
                    coll.Add(u);

                    dynamic downloader = session.CreateUpdateDownloader();
                    downloader.Updates = coll;
                    downloader.Download();

                    dynamic installer = session.CreateUpdateInstaller();
                    installer.Updates = coll;
                    dynamic res = installer.Install();

                    int rc = (int)res.ResultCode; // 2 = Succeeded, 3 = SucceededWithErrors
                    if (rc is 2 or 3) installed++; else failed++;
                    try { if ((bool)res.RebootRequired) reboot = true; } catch (Exception) { }
                }
                catch (Exception) { failed++; }

                progress?.Report(new WindowsUpdateProgress
                {
                    Current = i, Total = picks.Count, Title = pick.Title, Percent = i * 100.0 / picks.Count,
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new WindowsUpdateInstallResult
            {
                Installed = installed, Failed = failed, RebootRequired = reboot,
                Message = "Windows Update failed: " + ex.Message,
            };
        }

        string msg = $"Installed {installed} update(s)."
            + (failed > 0 ? $" {failed} could not be installed." : "")
            + (reboot ? " A restart is required to finish." : "");
        return new WindowsUpdateInstallResult { Installed = installed, Failed = failed, RebootRequired = reboot, Message = msg };
    });

    public Task<List<WindowsUpdateHistoryItem>> GetHistoryAsync() => Task.Run(() =>
    {
        var list = new List<WindowsUpdateHistoryItem>();
        try
        {
            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return list;

            dynamic session = Activator.CreateInstance(sessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();

            int count = 0;
            try { count = (int)searcher.GetTotalHistoryCount(); } catch (Exception) { }
            if (count <= 0) return list;

            dynamic history = searcher.QueryHistory(0, Math.Min(count, 50));
            foreach (dynamic h in history)
            {
                try
                {
                    string title = (string)(h.Title ?? "");
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    DateTime date = default;
                    try { date = Convert.ToDateTime(h.Date); } catch (Exception) { }
                    int rc = 0;
                    try { rc = (int)h.ResultCode; } catch (Exception) { }
                    list.Add(new WindowsUpdateHistoryItem { Title = title.Trim(), Date = date, Result = ResultText(rc) });
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return list;
    });

    public (bool Ok, string Message) Pause(int days)
    {
        try
        {
            days = Math.Clamp(days, 1, 35);
            using var key = Registry.LocalMachine.CreateSubKey(UxSettings);
            if (key is null) return (false, "Couldn't open the Windows Update settings.");

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string end = DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ssZ");
            key.SetValue("PauseUpdatesStartTime", now, RegistryValueKind.String);
            key.SetValue("PauseUpdatesExpiryTime", end, RegistryValueKind.String);
            key.SetValue("PauseFeatureUpdatesStartTime", now, RegistryValueKind.String);
            key.SetValue("PauseFeatureUpdatesEndTime", end, RegistryValueKind.String);
            key.SetValue("PauseQualityUpdatesStartTime", now, RegistryValueKind.String);
            key.SetValue("PauseQualityUpdatesEndTime", end, RegistryValueKind.String);
            return (true, $"Updates paused until {DateTime.Now.AddDays(days):d}.");
        }
        catch (Exception ex)
        {
            return (false, $"Couldn't pause updates: {ex.Message}");
        }
    }

    public (bool Ok, string Message) Resume()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UxSettings, writable: true);
            if (key is null) return (true, "Updates are not paused.");
            foreach (var name in PauseValues) key.DeleteValue(name, throwOnMissingValue: false);
            return (true, "Updates resumed.");
        }
        catch (Exception ex)
        {
            return (false, $"Couldn't resume updates: {ex.Message}");
        }
    }

    public void OpenWindowsUpdate()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private static string ResultText(int code) => code switch
    {
        2 => "Succeeded",
        3 => "Succeeded with errors",
        4 => "Failed",
        5 => "Cancelled",
        _ => "Unknown",
    };
}
