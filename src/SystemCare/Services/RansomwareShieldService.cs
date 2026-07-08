using System.Diagnostics;
using System.Management;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IRansomwareShieldService
{
    Task<RansomwareStatus> GetStatusAsync(CancellationToken ct = default);
    /// <summary>Turns Controlled Folder Access on or off via Set-MpPreference. Returns (ok, message).</summary>
    Task<(bool Ok, string Message)> SetEnabledAsync(bool enabled, CancellationToken ct = default);
    void OpenWindowsSecurity();
}

public class RansomwareShieldService : IRansomwareShieldService
{
    private readonly ILogService _log;

    public RansomwareShieldService(ILogService log) => _log = log;

    public async Task<RansomwareStatus> GetStatusAsync(CancellationToken ct = default)
    {
        // Fast path: read Defender preferences straight from WMI — no powershell.exe spawn (was two).
        var wmi = await Task.Run(TryGetStatusViaWmi, ct);
        if (wmi is not null) return wmi;
        // Fallback for configurations where the WMI class can't be queried.
        return await GetStatusViaPowerShellAsync(ct);
    }

    // MSFT_MpPreference exposes the same values Get-MpPreference reads. Reading them via
    // System.Management avoids launching PowerShell twice on every status refresh.
    private RansomwareStatus? TryGetStatusViaWmi()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(
                "SELECT EnableControlledFolderAccess, ControlledFolderAccessProtectedFolders FROM MSFT_MpPreference"));
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                using (mo)
                {
                    var raw = mo["EnableControlledFolderAccess"];
                    string state = raw is null
                        ? "Unknown"
                        : NormalizeState(Convert.ToInt32(raw).ToString());
                    var folders = (mo["ControlledFolderAccessProtectedFolders"] as string[])?
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .Select(f => f.Trim())
                        .ToList() ?? new List<string>();
                    return new RansomwareStatus { IsAvailable = true, State = state, ProtectedFolders = folders };
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _log.Warn("Ransomware", $"WMI status read failed, falling back to PowerShell: {ex.Message}");
            return null;
        }
    }

    private async Task<RansomwareStatus> GetStatusViaPowerShellAsync(CancellationToken ct)
    {
        try
        {
            var (stateCode, stateOut) = await ProcessRunner.RunPowerShellAsync(
                "(Get-MpPreference).EnableControlledFolderAccess", ct);
            if (stateCode != 0) return new RansomwareStatus { IsAvailable = false };

            string state = NormalizeState(stateOut.Trim());

            var (_, foldersOut) = await ProcessRunner.RunPowerShellAsync(
                "(Get-MpPreference).ControlledFolderAccessProtectedFolders", ct);
            var folders = foldersOut
                .Split('\n', '\r')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            return new RansomwareStatus { IsAvailable = true, State = state, ProtectedFolders = folders };
        }
        catch (Exception ex)
        {
            _log.Warn("Ransomware", $"Status read failed: {ex.Message}");
            return new RansomwareStatus { IsAvailable = false };
        }
    }

    // Get-MpPreference may print the enum name ("Enabled") or its numeric value (0/1/2).
    private static string NormalizeState(string raw) => raw switch
    {
        "0" => "Disabled",
        "1" => "Enabled",
        "2" => "AuditMode",
        "" => "Unknown",
        _ => raw,
    };

    public async Task<(bool Ok, string Message)> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        string value = enabled ? "Enabled" : "Disabled";
        try
        {
            var (code, output) = await ProcessRunner.RunPowerShellAsync(
                $"Set-MpPreference -EnableControlledFolderAccess {value}", ct);
            if (code == 0)
            {
                _log.Info("Ransomware", $"Controlled Folder Access set to {value}.");
                return (true, enabled
                    ? "Ransomware protection turned on."
                    : "Ransomware protection turned off.");
            }
            string msg = string.IsNullOrWhiteSpace(output) ? $"PowerShell exited {code}." : output.Trim();
            _log.Warn("Ransomware", msg);
            return (false, msg);
        }
        catch (Exception ex)
        {
            _log.Error("Ransomware", "Toggle failed", ex);
            return (false, ex.Message);
        }
    }

    public void OpenWindowsSecurity()
    {
        try { Process.Start(new ProcessStartInfo("windowsdefender://RansomwareProtection") { UseShellExecute = true }); }
        catch (Exception ex) { _log.Warn("Ransomware", $"Could not open Windows Security: {ex.Message}"); }
    }
}
