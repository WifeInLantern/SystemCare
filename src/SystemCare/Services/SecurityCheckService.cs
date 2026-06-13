using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ISecurityCheckService
{
    Task<List<SecurityCheck>> GetChecksAsync();
    void OpenFix(string target);
}

public class SecurityCheckService : ISecurityCheckService
{
    public Task<List<SecurityCheck>> GetChecksAsync() => Task.Run(() => new List<SecurityCheck>
    {
        CheckDefender(),
        CheckFirewall(),
        CheckUac(),
        CheckRemoteDesktop(),
        CheckWindowsUpdate(),
    });

    private static SecurityCheck CheckDefender()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge FROM MSFT_MpComputerStatus"));
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                bool av = mo["AntivirusEnabled"] as bool? ?? false;
                bool rtp = mo["RealTimeProtectionEnabled"] as bool? ?? false;
                if (av && rtp)
                    return new SecurityCheck { Name = "Microsoft Defender", Icon = "ShieldCheckmark24", Status = SecurityStatus.Ok, Message = "Antivirus and real-time protection are on." };
                return new SecurityCheck
                {
                    Name = "Microsoft Defender", Icon = "Shield24", Status = SecurityStatus.Warning,
                    Message = "Real-time protection is off (this is normal if another antivirus is installed).",
                    FixLabel = "Open Windows Security", FixTarget = "windowsdefender:",
                };
            }
        }
        catch (Exception) { }
        return new SecurityCheck { Name = "Microsoft Defender", Icon = "Shield24", Status = SecurityStatus.Unknown, Message = "Could not read Defender status (another antivirus may manage protection).", FixLabel = "Open Windows Security", FixTarget = "windowsdefender:" };
    }

    private static SecurityCheck CheckFirewall()
    {
        int enabled = 0, total = 0;
        foreach (var profile in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
                if (key?.GetValue("EnableFirewall") is int v) { total++; if (v == 1) enabled++; }
            }
            catch (Exception) { }
        }
        if (total > 0 && enabled == total)
            return new SecurityCheck { Name = "Windows Firewall", Icon = "ShieldCheckmark24", Status = SecurityStatus.Ok, Message = "The firewall is on for all network profiles." };
        return new SecurityCheck
        {
            Name = "Windows Firewall", Icon = "Shield24",
            Status = enabled == 0 ? SecurityStatus.Bad : SecurityStatus.Warning,
            Message = $"The firewall is off for {total - enabled} of {total} network profiles.",
            FixLabel = "Open Firewall settings", FixTarget = "firewall.cpl",
        };
    }

    private static SecurityCheck CheckUac()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            if (key?.GetValue("EnableLUA") is int v && v == 0)
                return new SecurityCheck { Name = "User Account Control", Icon = "Warning24", Status = SecurityStatus.Bad, Message = "UAC is turned off — apps can make changes without prompting.", FixLabel = "Open UAC settings", FixTarget = "UserAccountControlSettings.exe" };
        }
        catch (Exception) { }
        return new SecurityCheck { Name = "User Account Control", Icon = "ShieldCheckmark24", Status = SecurityStatus.Ok, Message = "UAC is on and will prompt for system changes." };
    }

    private static SecurityCheck CheckRemoteDesktop()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
            if (key?.GetValue("fDenyTSConnections") is int v && v == 0)
                return new SecurityCheck { Name = "Remote Desktop", Icon = "Warning24", Status = SecurityStatus.Warning, Message = "Remote Desktop is enabled — make sure that's intended.", FixLabel = "Open Remote Desktop settings", FixTarget = "ms-settings:remotedesktop" };
        }
        catch (Exception) { }
        return new SecurityCheck { Name = "Remote Desktop", Icon = "ShieldCheckmark24", Status = SecurityStatus.Ok, Message = "Remote Desktop is disabled." };
    }

    private static SecurityCheck CheckWindowsUpdate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
            if (key?.GetValue("LastSuccessTime") is string raw &&
                DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var last))
            {
                int days = (int)(DateTime.UtcNow - last.ToUniversalTime()).TotalDays;
                if (days <= 30)
                    return new SecurityCheck { Name = "Windows Update", Icon = "ShieldCheckmark24", Status = SecurityStatus.Ok, Message = $"Last updated {days} day(s) ago." };
                return new SecurityCheck { Name = "Windows Update", Icon = "Warning24", Status = SecurityStatus.Warning, Message = $"Last update was {days} days ago — check for updates.", FixLabel = "Open Windows Update", FixTarget = "ms-settings:windowsupdate" };
            }
        }
        catch (Exception) { }
        return new SecurityCheck { Name = "Windows Update", Icon = "Shield24", Status = SecurityStatus.Unknown, Message = "Update history unavailable — check manually.", FixLabel = "Open Windows Update", FixTarget = "ms-settings:windowsupdate" };
    }

    public void OpenFix(string target)
    {
        try
        {
            if (target.Equals("firewall.cpl", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("control.exe", "firewall.cpl") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
