using System.Diagnostics;
using System.Management;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IBatteryHealthService
{
    /// <summary>Reads a battery health snapshot via WMI. Returns HasBattery=false on desktops.</summary>
    Task<BatteryReport> GetReportAsync();

    /// <summary>
    /// Generates Windows' detailed HTML battery report (powercfg /batteryreport) and opens it.
    /// Returns the report path, or null if it could not be produced.
    /// </summary>
    Task<string?> ExportDetailedReportAsync();
}

public class BatteryHealthService : IBatteryHealthService
{
    private readonly ILogService _log;

    public BatteryHealthService(ILogService log) => _log = log;

    public Task<BatteryReport> GetReportAsync() => Task.Run(() =>
    {
        long design = 0, full = 0;
        int cycles = 0, charge = -1;
        string name = "", manufacturer = "", chemistry = "";
        bool onAc = false;
        bool present = false;

        // Capacities live in root\WMI (design vs. full-charge), the same data powercfg reports on.
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            scope.Connect();

            foreach (ManagementBaseObject mo in Query(scope, "SELECT * FROM BatteryStaticData"))
            {
                present = true;
                design = ToLong(mo, "DesignedCapacity");
                manufacturer = mo["ManufactureName"] as string ?? "";
                name = mo["DeviceName"] as string ?? "";
                chemistry = ReadChemistry(mo["Chemistry"]);
            }

            foreach (ManagementBaseObject mo in Query(scope, "SELECT * FROM BatteryFullChargedCapacity"))
                full = ToLong(mo, "FullChargedCapacity");

            foreach (ManagementBaseObject mo in Query(scope, "SELECT * FROM BatteryCycleCount"))
                cycles = (int)ToLong(mo, "CycleCount");
        }
        catch (Exception ex)
        {
            _log.Warn("Battery", $"root\\WMI battery query failed: {ex.Message}");
        }

        // Current charge % and AC state come from Win32_Battery (root\CIMV2).
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT EstimatedChargeRemaining, BatteryStatus, Chemistry, Name FROM Win32_Battery");
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                present = true;
                charge = (int)(mo["EstimatedChargeRemaining"] as ushort? ?? 0);
                // BatteryStatus 2 == "AC connected" per the Win32_Battery schema.
                onAc = (mo["BatteryStatus"] as ushort? ?? 0) == 2;
                if (string.IsNullOrEmpty(name)) name = mo["Name"] as string ?? "";
                if (string.IsNullOrEmpty(chemistry)) chemistry = ReadWin32Chemistry(mo["Chemistry"]);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Battery", $"Win32_Battery query failed: {ex.Message}");
        }

        if (!present)
            return new BatteryReport { HasBattery = false };

        return new BatteryReport
        {
            HasBattery = true,
            Name = string.IsNullOrWhiteSpace(name) ? "Battery" : name,
            Manufacturer = manufacturer,
            Chemistry = chemistry,
            DesignCapacityMilliWattHours = design,
            FullChargeCapacityMilliWattHours = full,
            CycleCount = cycles,
            ChargePercent = charge,
            OnAcPower = onAc,
        };
    });

    private static ManagementObjectCollection Query(ManagementScope scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
        return searcher.Get();
    }

    private static long ToLong(ManagementBaseObject mo, string field)
    {
        try
        {
            return mo[field] switch
            {
                uint u => u,
                int i => i,
                long l => l,
                ushort s => s,
                _ => Convert.ToInt64(mo[field] ?? 0),
            };
        }
        catch (Exception) { return 0; }
    }

    // root\WMI BatteryStaticData.Chemistry is a 4-char code (e.g. "LION", "LIP").
    private static string ReadChemistry(object? raw)
    {
        try
        {
            if (raw is byte[] bytes) return System.Text.Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
            if (raw is string s) return s.Trim();
        }
        catch (Exception) { }
        return "";
    }

    // Win32_Battery.Chemistry is a numeric enum.
    private static string ReadWin32Chemistry(object? raw) => (raw as ushort? ?? 0) switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Lead Acid",
        4 => "Nickel Cadmium",
        5 => "Nickel Metal Hydride",
        6 => "Lithium-ion",
        7 => "Zinc Air",
        8 => "Lithium Polymer",
        _ => "",
    };

    public async Task<string?> ExportDetailedReportAsync()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"SystemCare-BatteryReport-{DateTime.Now:yyyyMMdd-HHmmss}.html");

            var psi = new ProcessStartInfo("powercfg.exe", $"/batteryreport /output \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.SystemDirectory,
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                _log.Info("Battery", $"Exported battery report to {path}");
                return path;
            }

            _log.Warn("Battery", $"powercfg battery report failed (exit {process.ExitCode}).");
        }
        catch (Exception ex)
        {
            _log.Warn("Battery", $"Could not export battery report: {ex.Message}");
        }
        return null;
    }
}
