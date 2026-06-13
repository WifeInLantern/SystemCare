using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ITweaksService
{
    IReadOnlyList<Tweak> Tweaks { get; }
    bool IsEnabled(string id);
    void SetEnabled(string id, bool enabled);
    void RestartExplorer();
}

public class TweaksService : ITweaksService
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string VisualFxKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string SerializeKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
    private const string ClassicMenuKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string DataCollectionKey = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";

    public IReadOnlyList<Tweak> Tweaks { get; } =
    [
        new Tweak { Id = "show-extensions", Group = "File Explorer", Name = "Show file extensions",
            Description = "Always display extensions like .exe and .txt", RequiresExplorerRestart = true },
        new Tweak { Id = "show-hidden", Group = "File Explorer", Name = "Show hidden files",
            Description = "Reveal hidden files and folders", RequiresExplorerRestart = true },
        new Tweak { Id = "classic-context-menu", Group = "File Explorer", Name = "Classic right-click menu (Windows 11)",
            Description = "Restore the full context menu instead of 'Show more options'", RequiresExplorerRestart = true },
        new Tweak { Id = "visual-fx-performance", Group = "Performance", Name = "Optimize visual effects for performance",
            Description = "Disable animations and shadows for a snappier feel", RequiresExplorerRestart = true },
        new Tweak { Id = "startup-delay-off", Group = "Performance", Name = "Disable startup app delay",
            Description = "Let startup programs launch without the built-in delay" },
        new Tweak { Id = "telemetry-off", Group = "Privacy", Name = "Minimize telemetry",
            Description = "Set diagnostic data to the lowest level and stop the DiagTrack service" },
    ];

    public bool IsEnabled(string id) => id switch
    {
        "show-extensions" => ReadInt(Registry.CurrentUser, AdvancedKey, "HideFileExt", 1) == 0,
        "show-hidden" => ReadInt(Registry.CurrentUser, AdvancedKey, "Hidden", 2) == 1,
        "classic-context-menu" => Registry.CurrentUser.OpenSubKey(ClassicMenuKey) is not null,
        "visual-fx-performance" => ReadInt(Registry.CurrentUser, VisualFxKey, "VisualFXSetting", 0) == 2,
        "startup-delay-off" => ReadInt(Registry.CurrentUser, SerializeKey, "StartupDelayInMSec", -1) == 0,
        "telemetry-off" => ReadInt(Registry.LocalMachine, DataCollectionKey, "AllowTelemetry", -1) == 0,
        _ => false,
    };

    public void SetEnabled(string id, bool enabled)
    {
        try
        {
            switch (id)
            {
                case "show-extensions":
                    WriteInt(Registry.CurrentUser, AdvancedKey, "HideFileExt", enabled ? 0 : 1);
                    break;
                case "show-hidden":
                    WriteInt(Registry.CurrentUser, AdvancedKey, "Hidden", enabled ? 1 : 2);
                    break;
                case "classic-context-menu":
                    if (enabled)
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(ClassicMenuKey);
                        key?.SetValue(string.Empty, string.Empty, RegistryValueKind.String);
                    }
                    else
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(
                            @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", throwOnMissingSubKey: false);
                    }
                    break;
                case "visual-fx-performance":
                    WriteInt(Registry.CurrentUser, VisualFxKey, "VisualFXSetting", enabled ? 2 : 0);
                    break;
                case "startup-delay-off":
                    if (enabled) WriteInt(Registry.CurrentUser, SerializeKey, "StartupDelayInMSec", 0);
                    else DeleteValue(Registry.CurrentUser, SerializeKey, "StartupDelayInMSec");
                    break;
                case "telemetry-off":
                    if (enabled)
                    {
                        WriteInt(Registry.LocalMachine, DataCollectionKey, "AllowTelemetry", 0);
                        SetDiagTrack(false);
                    }
                    else
                    {
                        DeleteValue(Registry.LocalMachine, DataCollectionKey, "AllowTelemetry");
                        SetDiagTrack(true);
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // best-effort; a failed tweak shouldn't crash the app
        }
    }

    public void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
                using (p) p.Kill();
            // Windows relaunches Explorer automatically; nudge it in case it doesn't.
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    private static void SetDiagTrack(bool enabled)
    {
        try
        {
            // Start type: 2 = automatic, 4 = disabled.
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack", writable: true);
            key?.SetValue("Start", enabled ? 2 : 4, RegistryValueKind.DWord);
            if (!enabled)
            {
                using var sc = new ServiceController("DiagTrack");
                if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                }
            }
        }
        catch (Exception) { }
    }

    private static int ReadInt(RegistryKey root, string path, string name, int fallback)
    {
        try { using var key = root.OpenSubKey(path); return key?.GetValue(name) is int v ? v : fallback; }
        catch (Exception) { return fallback; }
    }

    private static void WriteInt(RegistryKey root, string path, string name, int value)
    {
        using var key = root.CreateSubKey(path);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void DeleteValue(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
