using System.Diagnostics;
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
