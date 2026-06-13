using System.Diagnostics;
using System.Management;
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

    public Task<(bool Ok, string Message)> CreateRestorePointAsync(string description) => Task.Run(() =>
    {
        try
        {
            var scope = new ManagementScope(ScopePath);
            scope.Connect();
            using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var inParams = cls.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
            inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
            using var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
            uint rv = ToUInt(outParams["ReturnValue"]);
            return rv switch
            {
                0 => (true, "Restore point created."),
                1058 => (false, "System Protection is turned off — enable it in System Properties to use restore points."),
                _ => (false, "Couldn't create a restore point. A recent one may already exist (Windows limits this to about once per 24 hours)."),
            };
        }
        catch (Exception)
        {
            return (false, "System Protection appears to be off for this PC. Turn it on in System Properties → System Protection.");
        }
    });

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
