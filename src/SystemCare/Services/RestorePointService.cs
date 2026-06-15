using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IRestorePointService
{
    Task<List<RestorePoint>> GetRestorePointsAsync();
    Task<(bool Ok, string Message)> CreateRestorePointAsync(string description);
    void OpenSystemRestore();
}

public class RestorePointService : IRestorePointService
{
    // SystemRestore lives in root\default and needs elevation + System Protection enabled on C:.
    private const string ScopePath = @"\\.\root\default";

    public Task<List<RestorePoint>> GetRestorePointsAsync() => Task.Run(() =>
    {
        var points = new List<RestorePoint>();
        try
        {
            var scope = new ManagementScope(ScopePath);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM SystemRestore"));
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                DateTime created = default;
                try { created = ManagementDateTimeConverter.ToDateTime(mo["CreationTime"]?.ToString()); }
                catch (Exception) { }
                points.Add(new RestorePoint
                {
                    SequenceNumber = ToUInt(mo["SequenceNumber"]),
                    Description = mo["Description"]?.ToString() ?? "",
                    CreationTime = created,
                    TypeText = MapType(ToInt(mo["RestorePointType"])),
                });
            }
        }
        catch (Exception)
        {
            // SR provider unavailable / protection off — return what we have (empty).
        }
        return points.OrderByDescending(p => p.CreationTime).ToList();
    });

    public Task<(bool Ok, string Message)> CreateRestorePointAsync(string description) => Task.Run<(bool, string)>(() =>
    {
        try
        {
            // Windows throttles restore-point creation to once per ~24h via
            // SystemRestorePointCreationFrequency. While throttled, CreateRestorePoint returns 0
            // (success) but silently creates nothing — so an explicit request appears to work yet
            // produces no point. Setting the limit to 0 makes every request actually run.
            TryDisableCreationFrequencyLimit();

            var scope = new ManagementScope(ScopePath);
            scope.Connect();

            int before = LatestSequence(scope);

            using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var inParams = cls.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
            inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
            using var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
            uint rv = ToUInt(outParams["ReturnValue"]);

            if (rv == 1058)
                return (false, "System Protection is turned off — enable it for your system drive in System Properties → System Protection.");
            if (rv != 0)
                return (false, $"Windows couldn't create a restore point (error {rv}). Make sure System Protection is on for the system drive.");

            // A 0 return doesn't guarantee a point was written, so confirm a new one actually appeared.
            int after = LatestSequence(scope);
            if (after > before)
                return (true, "Restore point created.");

            return (false, "Windows reported success but created no restore point. This usually means System Protection is off for the system drive, or too little disk space is reserved for restore points.");
        }
        catch (Exception ex)
        {
            return (false, $"Couldn't create a restore point — System Protection may be off. Turn it on in System Properties → System Protection. ({ex.Message})");
        }
    });

    /// <summary>Highest restore-point sequence number currently on the system, or -1 if none/unreadable.</summary>
    private static int LatestSequence(ManagementScope scope)
    {
        int max = -1;
        try
        {
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT SequenceNumber FROM SystemRestore"));
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                int seq = ToInt(mo["SequenceNumber"]);
                if (seq > max) max = seq;
            }
        }
        catch (Exception) { }
        return max;
    }

    /// <summary>Removes the once-per-24h throttle so an explicit restore-point request always runs.</summary>
    private static void TryDisableCreationFrequencyLimit()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            key?.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
        }
        catch (Exception) { }
    }

    public void OpenSystemRestore()
    {
        try
        {
            Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    private static string MapType(int type) => type switch
    {
        0 => "Application install",
        1 => "Application uninstall",
        10 => "Driver install",
        12 => "Settings change",
        13 => "Cancelled operation",
        _ => "Restore point",
    };

    private static uint ToUInt(object? o)
    {
        try { return o is null ? 0 : Convert.ToUInt32(o); } catch (Exception) { return 0; }
    }

    private static int ToInt(object? o)
    {
        try { return o is null ? -1 : Convert.ToInt32(o); } catch (Exception) { return -1; }
    }
}
