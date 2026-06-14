using System.Diagnostics;
using System.Management;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IDriverUpdateService
{
    /// <summary>Inventory of installed devices + their current drivers (WMI). Flags problem devices.</summary>
    Task<List<DriverDevice>> GetInstalledDriversAsync();
    /// <summary>Searches Windows Update for newer drivers. Read-only; never throws. Caches results for install.</summary>
    Task<List<DriverUpdate>> CheckForUpdatesAsync(CancellationToken ct);
    /// <summary>Downloads + installs the selected updates (from the last check). Needs elevation.</summary>
    Task<DriverInstallResult> InstallAsync(IEnumerable<DriverUpdate> updates, IProgress<DriverInstallProgress>? progress, CancellationToken ct);
    void OpenWindowsUpdate();
    void OpenDeviceManager();
}

/// <summary>
/// Driver inventory via WMI and driver updates via the Windows Update Agent (WUA) COM API
/// (late-bound through <c>dynamic</c>, so no interop package is needed). This is the safe,
/// built-in path Windows itself uses for "optional driver updates" — it only offers drivers
/// Microsoft distributes, and does not scrape third-party sites.
/// </summary>
public class DriverUpdateService : IDriverUpdateService
{
    // Microsoft Update service GUID — opting in broadens driver coverage beyond core WU.
    private const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";

    // COM update objects from the last search, addressed by DriverUpdate.Index.
    private readonly List<object> _lastUpdates = [];

    public Task<List<DriverDevice>> GetInstalledDriversAsync() => Task.Run(() =>
    {
        // Map problem devices (ConfigManagerErrorCode != 0) by DeviceID.
        var problems = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mo in Query("Win32_PnPEntity"))
        {
            int code = Int(mo, "ConfigManagerErrorCode");
            string id = Str(mo, "DeviceID");
            if (code != 0 && id.Length > 0) problems[id] = code;
        }

        var list = new List<DriverDevice>();
        var seen = new HashSet<string>();
        foreach (var mo in Query("Win32_PnPSignedDriver"))
        {
            string name = Str(mo, "DeviceName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            string ver = Str(mo, "DriverVersion");
            if (!seen.Add(name + "|" + ver)) continue; // dedupe identical rows

            string id = Str(mo, "DeviceID");
            DateTime? date = null;
            string rawDate = Str(mo, "DriverDate");
            if (rawDate.Length > 0)
            {
                try { date = ManagementDateTimeConverter.ToDateTime(rawDate); } catch (Exception) { }
            }

            bool problem = id.Length > 0 && problems.TryGetValue(id, out int code);
            list.Add(new DriverDevice
            {
                Name = name.Trim(),
                DeviceClass = Str(mo, "DeviceClass"),
                Manufacturer = Str(mo, "Manufacturer").Trim(),
                DriverVersion = ver,
                DriverDate = date,
                DeviceId = id,
                HasProblem = problem,
                ProblemText = problem ? ProblemTextFor(problems[id]) : "",
            });
        }

        return list
            .OrderByDescending(d => d.HasProblem)
            .ThenBy(d => d.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    public Task<List<DriverUpdate>> CheckForUpdatesAsync(CancellationToken ct) => Task.Run(() =>
    {
        var results = new List<DriverUpdate>();
        _lastUpdates.Clear();

        try
        {
            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return results;

            dynamic session = Activator.CreateInstance(sessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();
            TryEnableMicrosoftUpdate(searcher);

            ct.ThrowIfCancellationRequested();
            dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Driver'");
            dynamic updates = searchResult.Updates;

            int index = 0;
            foreach (dynamic u in updates)
            {
                ct.ThrowIfCancellationRequested();

                long size = 0;
                try { size = Convert.ToInt64(u.MaxDownloadSize); } catch (Exception) { }
                string mfr = "", cls = "";
                DateTime? date = null;
                try { mfr = (string)(u.DriverManufacturer ?? ""); } catch (Exception) { }
                try { cls = (string)(u.DriverClass ?? ""); } catch (Exception) { }
                try { var d = u.DriverVerDate; if (d is not null) date = Convert.ToDateTime(d); } catch (Exception) { }
                string id = "";
                try { id = (string)u.Identity.UpdateID; } catch (Exception) { }

                _lastUpdates.Add(u);
                results.Add(new DriverUpdate
                {
                    Title = ((string)u.Title).Trim(),
                    Manufacturer = mfr,
                    DriverClass = cls,
                    DriverDate = date,
                    SizeBytes = size,
                    UpdateId = id,
                    Index = index++,
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // WU service unavailable / offline / blocked — treat as "no updates".
        }

        return results;
    });

    public Task<DriverInstallResult> InstallAsync(IEnumerable<DriverUpdate> updates,
        IProgress<DriverInstallProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var picks = updates.Where(u => u.Index >= 0 && u.Index < _lastUpdates.Count).ToList();
        if (picks.Count == 0)
            return new DriverInstallResult { Message = "No driver updates selected." };

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
                progress?.Report(new DriverInstallProgress
                {
                    Current = i, Total = picks.Count, Title = pick.Title,
                    Percent = (i - 1) * 100.0 / picks.Count,
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

                progress?.Report(new DriverInstallProgress
                {
                    Current = i, Total = picks.Count, Title = pick.Title,
                    Percent = i * 100.0 / picks.Count,
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new DriverInstallResult
            {
                Installed = installed, Failed = failed, RebootRequired = reboot,
                Message = "Driver update failed: " + ex.Message,
            };
        }

        string msg = $"Installed {installed} driver update(s)."
            + (failed > 0 ? $" {failed} could not be installed." : "")
            + (reboot ? " A restart is required to finish." : "");
        return new DriverInstallResult { Installed = installed, Failed = failed, RebootRequired = reboot, Message = msg };
    });

    public void OpenWindowsUpdate() => OpenShell("ms-settings:windowsupdate");
    public void OpenDeviceManager() => OpenShell("devmgmt.msc");

    // ---- helpers ----

    private static void TryEnableMicrosoftUpdate(dynamic searcher)
    {
        try
        {
            Type? smType = Type.GetTypeFromProgID("Microsoft.Update.ServiceManager");
            if (smType is null) return;
            dynamic sm = Activator.CreateInstance(smType)!;
            sm.AddService2(MicrosoftUpdateServiceId, 7 /* registered + opt-in */, "");
            searcher.ServerSelection = 3; // ssOthers
            searcher.ServiceID = MicrosoftUpdateServiceId;
        }
        catch (Exception)
        {
            // Fall back to the default WU service (still returns drivers published to WU).
        }
    }

    private static string ProblemTextFor(int code) => code switch
    {
        1 => "Not configured correctly",
        10 => "Cannot start",
        18 => "Reinstall the drivers",
        28 => "Drivers not installed",
        31 => "Not working properly",
        37 => "Driver returned a failure",
        39 => "Driver may be corrupted or missing",
        43 => "Windows stopped this device (reported problems)",
        _ => $"Device problem (code {code})",
    };

    private static void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private static IEnumerable<ManagementObject> Query(string className)
    {
        ManagementObjectCollection results;
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}")
            {
                Options = { Timeout = TimeSpan.FromSeconds(20) },
            };
            results = searcher.Get();
        }
        catch (Exception)
        {
            yield break;
        }
        foreach (ManagementBaseObject mo in results)
            yield return (ManagementObject)mo;
    }

    private static string Str(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop]?.ToString() ?? ""; } catch (Exception) { return ""; }
    }

    private static int Int(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop] is null ? 0 : Convert.ToInt32(mo[prop]); } catch (Exception) { return 0; }
    }
}
