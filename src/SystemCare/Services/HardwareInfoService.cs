using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IHardwareInfoService
{
    /// <summary>One cached WMI sweep of the machine's hardware. Slow on first call; cached after.</summary>
    Task<HardwareReport> GetReportAsync();
}

public class HardwareInfoService : IHardwareInfoService
{
    private HardwareReport? _cached;

    public Task<HardwareReport> GetReportAsync() => Task.Run(() =>
    {
        if (_cached is not null) return _cached;

        var report = new HardwareReport();
        AddOperatingSystem(report);
        foreach (var mo in Query("Win32_Processor")) AddProcessor(report, mo);
        AddMemory(report);
        foreach (var mo in Query("Win32_VideoController")) AddGraphics(report, mo);
        foreach (var mo in Query("Win32_DiskDrive")) AddDisk(report, mo);
        AddMotherboard(report);
        AddBatteries(report);

        _cached = report;
        return report;
    });

    private static void AddOperatingSystem(HardwareReport report)
    {
        string caption = QuerySingle("Win32_OperatingSystem", "Caption") ?? "Windows";
        string display = ReadCurrentVersion("DisplayVersion") ?? ReadCurrentVersion("ReleaseId") ?? "";
        string build = OsBuildString();
        string arch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        report.Specs.Add(new HardwareSpec
        {
            Category = "Operating system", Section = HardwareSection.Os, Icon = "Desktop24",
            Name = caption.Trim(),
            Detail = TextHelpers.JoinParts(
                display.Length > 0 ? $"Version {display} (build {build})" : $"Build {build}",
                arch, $"Machine {Environment.MachineName}"),
            Tooltip = TextHelpers.JoinParts(caption.Trim(), OsInstallDate(), $"Signed in as {Environment.UserName}"),
        });
    }

    private static void AddProcessor(HardwareReport report, ManagementObject mo)
    {
        string name = Str(mo, "Name").Trim();
        if (name.Length == 0) return;
        int cores = Int(mo, "NumberOfCores");
        int logical = Int(mo, "NumberOfLogicalProcessors");
        if (logical == 0) logical = Environment.ProcessorCount; // WMI can return 0 on some VMs
        int mhz = Int(mo, "MaxClockSpeed");

        report.Specs.Add(new HardwareSpec
        {
            Category = "Processor", Section = HardwareSection.Cpu, Icon = "DeveloperBoard24",
            Name = name,
            Detail = TextHelpers.JoinParts(
                cores > 0 ? $"{cores} cores" : null,
                logical > 0 ? $"{logical} threads" : null,
                mhz > 0 ? $"{mhz / 1000.0:0.0} GHz base" : null),
            Tooltip = TextHelpers.JoinParts(Str(mo, "Manufacturer"), Str(mo, "Description")),
        });
    }

    private static void AddMemory(HardwareReport report)
    {
        var modules = Query("Win32_PhysicalMemory").ToList();
        if (modules.Count == 0) return;

        long total = modules.Sum(m => Long(m, "Capacity"));
        int speed = modules.Select(m => Int(m, "Speed")).DefaultIfEmpty(0).Max();
        string? type = MemoryTypeName(modules.Select(m => Int(m, "SMBIOSMemoryType")).DefaultIfEmpty(0).Max());
        var perModule = modules.Select(m => TextHelpers.JoinParts(
            ByteFormatter.Format(Long(m, "Capacity")),
            Str(m, "Manufacturer"),
            Int(m, "Speed") > 0 ? $"{Int(m, "Speed")} MHz" : null,
            Str(m, "DeviceLocator")));

        report.Specs.Add(new HardwareSpec
        {
            Category = "Memory", Section = HardwareSection.Ram, Icon = "Memory16",
            Name = $"{ByteFormatter.Format(total)} RAM",
            Detail = TextHelpers.JoinParts(
                $"{modules.Count} module(s)",
                type,
                speed > 0 ? $"{speed} MHz" : null),
            Tooltip = string.Join("\n", perModule),
        });
    }

    private static readonly string[] VirtualGpuMarkers =
    [
        "microsoft basic", "microsoft remote", "remote display", "rdp", "parsec", "displaylink",
        "mirror", "virtual", "vmware svga", "citrix", "oray", "meta virtual", "spacedesk",
    ];

    private static void AddGraphics(HardwareReport report, ManagementObject mo)
    {
        string name = Str(mo, "Name").Trim();
        if (name.Length == 0) return;
        // Skip software/remote/mirror adapters so only real GPUs are listed.
        if (VirtualGpuMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase))) return;

        long vram = ResolveVramBytes(name, Long(mo, "AdapterRAM"));
        string driver = Str(mo, "DriverVersion").Trim();
        int w = Int(mo, "CurrentHorizontalResolution"), h = Int(mo, "CurrentVerticalResolution"), hz = Int(mo, "CurrentRefreshRate");

        report.Specs.Add(new HardwareSpec
        {
            Category = "Graphics", Section = HardwareSection.Gpu, Icon = "VideoClip24",
            Name = name,
            Detail = TextHelpers.JoinParts(
                vram > 0 ? $"{ByteFormatter.Format(vram)} VRAM" : null,
                driver.Length > 0 ? $"driver {driver}" : null,
                w > 0 && h > 0 ? $"{w}×{h}" + (hz > 0 ? $" @ {hz} Hz" : "") : null),
            DriverVersion = driver.Length > 0 ? driver : null,
            Tooltip = TextHelpers.JoinParts(Str(mo, "VideoProcessor"), Str(mo, "AdapterCompatibility")),
        });
    }

    private static void AddDisk(HardwareReport report, ManagementObject mo)
    {
        string model = Str(mo, "Model").Trim();
        if (model.Length == 0) return;
        long size = Long(mo, "Size");
        string status = Str(mo, "Status").Trim();       // "OK" / "Pred Fail" / "Degraded" …
        string iface = Str(mo, "InterfaceType").Trim();

        report.Specs.Add(new HardwareSpec
        {
            Category = "Disk", Section = HardwareSection.Storage, Icon = "HardDrive24",
            Name = model,
            Detail = TextHelpers.JoinParts(
                size > 0 ? ByteFormatter.Format(size) : null,
                iface.Length > 0 ? iface : null),
            Health = status.Length == 0 ? null : (status.Equals("OK", StringComparison.OrdinalIgnoreCase) ? "Healthy" : status),
            Tooltip = TextHelpers.JoinParts(Str(mo, "SerialNumber").Trim(), Str(mo, "MediaType")),
        });
    }

    private static void AddMotherboard(HardwareReport report)
    {
        foreach (var mo in Query("Win32_BaseBoard"))
        {
            string name = $"{Str(mo, "Manufacturer")} {Str(mo, "Product")}".Trim();
            if (name.Length == 0) return;
            string version = Str(mo, "Version").Trim();
            report.Specs.Add(new HardwareSpec
            {
                Category = "Motherboard", Section = HardwareSection.Board, Icon = "Board24",
                Name = name,
                Detail = TextHelpers.JoinParts(
                    version.Length > 0 ? $"Rev {version}" : null,
                    QuerySingle("Win32_BIOS", "SMBIOSBIOSVersion") is string bios && bios.Length > 0 ? $"BIOS {bios}" : null),
            });
            return;
        }
    }

    private static void AddBatteries(HardwareReport report)
    {
        // Desktops have no Win32_Battery instances, so this section simply doesn't appear there.
        foreach (var mo in Query("Win32_Battery"))
        {
            int charge = Int(mo, "EstimatedChargeRemaining");
            string name = Str(mo, "Name").Trim();
            report.Specs.Add(new HardwareSpec
            {
                Category = "Battery", Section = HardwareSection.Battery, Icon = "Battery1024",
                Name = name.Length > 0 ? name : "Battery",
                Detail = TextHelpers.JoinParts(BatteryStatusName(Int(mo, "BatteryStatus")),
                    charge > 0 ? $"{charge}% charged" : null),
                Health = charge > 0 ? $"{charge}%" : null,
            });
        }
    }

    // ---- small lookups & formatters ----

    private static string OsBuildString()
    {
        string build = ReadCurrentVersion("CurrentBuildNumber") ?? Environment.OSVersion.Version.Build.ToString();
        string? ubr = ReadCurrentVersionDword("UBR");
        return ubr is null ? build : $"{build}.{ubr}";
    }

    private static string OsInstallDate()
    {
        try
        {
            foreach (var mo in Query("Win32_OperatingSystem"))
            {
                string raw = Str(mo, "InstallDate");
                if (raw.Length == 0) return "";
                var when = System.Management.ManagementDateTimeConverter.ToDateTime(raw);
                return $"Installed {when:d MMM yyyy}";
            }
        }
        catch (Exception) { }
        return "";
    }

    private static string? ReadCurrentVersion(string value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue(value) as string is { Length: > 0 } s ? s : null;
        }
        catch (Exception) { return null; }
    }

    private static string? ReadCurrentVersionDword(string value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue(value) is int i ? i.ToString() : null;
        }
        catch (Exception) { return null; }
    }

    private static string? MemoryTypeName(int smbiosType) => smbiosType switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5",
        _ => null,
    };

    private static string BatteryStatusName(int status) => status switch
    {
        1 => "Discharging", 2 => "On AC power", 3 => "Fully charged",
        4 => "Low", 5 => "Critical", 6 => "Charging", 7 => "Charging (high)",
        8 => "Charging (low)", 9 => "Charging (critical)", 11 => "Partially charged",
        _ => "",
    };

    /// <summary>
    /// Win32_VideoController.AdapterRAM is a <c>uint32</c>, so it caps at ~4 GB and under-reports any
    /// larger GPU (e.g. a 12 GB card shows 4 GB). The true size is exposed as a 64-bit
    /// <c>HardwareInformation.qwMemorySize</c> on the adapter's registry key — read that instead.
    /// </summary>
    private static long ResolveVramBytes(string gpuName, long adapterRam)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return adapterRam;

            long best = adapterRam;
            foreach (var sub in classKey.GetSubKeyNames())
            {
                if (!Regex.IsMatch(sub, @"^\d{4}$")) continue; // adapter instances: 0000, 0001, …
                using var k = classKey.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                if (!NameMatches(desc, gpuName)) continue;

                long qw = ReadQwMemorySize(k);
                if (qw > best) best = qw;
            }
            return best;
        }
        catch (Exception)
        {
            return adapterRam;
        }
    }

    /// <summary>
    /// Reads <c>HardwareInformation.qwMemorySize</c>, which different drivers store as a REG_QWORD
    /// (returned as <see cref="long"/>) or a REG_BINARY 8-byte little-endian blob. Returns 0 if absent
    /// or unreadable so the caller keeps its existing best value.
    /// </summary>
    private static long ReadQwMemorySize(RegistryKey adapterKey)
    {
        try
        {
            object? raw = adapterKey.GetValue("HardwareInformation.qwMemorySize");
            return raw switch
            {
                long l => l,
                int i => i,
                byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                _ => 0,
            };
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static bool NameMatches(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
        b.Contains(a, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ManagementObject> Query(string className)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}")
            {
                Options = { Timeout = TimeSpan.FromSeconds(8) },
            };
            using var results = searcher.Get();
            // Materialize while the searcher/collection are still alive - returning the
            // collection itself after disposal is undefined behavior (see BatteryHealthService.ForEach).
            return results.Cast<ManagementBaseObject>().Cast<ManagementObject>().ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string? QuerySingle(string className, string property) =>
        Query(className).Select(mo => Str(mo, property)).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static string Str(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop]?.ToString() ?? ""; } catch (Exception) { return ""; }
    }

    private static int Int(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop] is null ? 0 : Convert.ToInt32(mo[prop]); } catch (Exception) { return 0; }
    }

    private static long Long(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop] is null ? 0 : Convert.ToInt64(mo[prop]); } catch (Exception) { return 0; }
    }
}
