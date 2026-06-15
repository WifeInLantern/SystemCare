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

        // OS — from Environment (instant, no WMI)
        report.Specs.Add(new HardwareSpec
        {
            Category = "Operating system", Icon = "Desktop24",
            Name = QuerySingle("Win32_OperatingSystem", "Caption") ?? "Windows",
            Detail = $"Build {Environment.OSVersion.Version.Build} · {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} · machine {Environment.MachineName}",
        });

        // CPU
        foreach (var mo in Query("Win32_Processor"))
        {
            string name = Str(mo, "Name");
            int cores = Int(mo, "NumberOfCores");
            int logical = Int(mo, "NumberOfLogicalProcessors");
            int mhz = Int(mo, "MaxClockSpeed");
            report.Specs.Add(new HardwareSpec
            {
                Category = "Processor", Icon = "DeveloperBoard24",
                Name = name.Trim(),
                Detail = $"{cores} cores · {logical} threads · {mhz / 1000.0:0.0} GHz",
            });
        }

        // RAM modules
        var ramModules = Query("Win32_PhysicalMemory").ToList();
        if (ramModules.Count > 0)
        {
            long totalBytes = ramModules.Sum(m => Long(m, "Capacity"));
            int speed = ramModules.Select(m => Int(m, "Speed")).DefaultIfEmpty(0).Max();
            report.Specs.Add(new HardwareSpec
            {
                Category = "Memory", Icon = "Ram24",
                Name = $"{ByteFormatter.Format(totalBytes)} RAM",
                Detail = $"{ramModules.Count} module(s)" + (speed > 0 ? $" · {speed} MHz" : ""),
            });
        }

        // GPU
        foreach (var mo in Query("Win32_VideoController"))
        {
            string name = Str(mo, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            long vram = ResolveVramBytes(name, Long(mo, "AdapterRAM"));
            report.Specs.Add(new HardwareSpec
            {
                Category = "Graphics", Icon = "VideoClip24",
                Name = name.Trim(),
                Detail = (vram > 0 ? $"{ByteFormatter.Format(vram)} VRAM · " : "") + $"driver {Str(mo, "DriverVersion")}",
            });
        }

        // Motherboard
        foreach (var mo in Query("Win32_BaseBoard"))
        {
            report.Specs.Add(new HardwareSpec
            {
                Category = "Motherboard", Icon = "Board24",
                Name = $"{Str(mo, "Manufacturer")} {Str(mo, "Product")}".Trim(),
                Detail = Str(mo, "Version"),
            });
            break;
        }

        // Physical disks
        foreach (var mo in Query("Win32_DiskDrive"))
        {
            string model = Str(mo, "Model");
            if (string.IsNullOrWhiteSpace(model)) continue;
            long size = Long(mo, "Size");
            report.Specs.Add(new HardwareSpec
            {
                Category = "Disk", Icon = "HardDrive24",
                Name = model.Trim(),
                Detail = (size > 0 ? ByteFormatter.Format(size) : "") + $" · {Str(mo, "InterfaceType")}",
            });
        }

        _cached = report;
        return report;
    });

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

                if (k.GetValue("HardwareInformation.qwMemorySize") is long qw && qw > best)
                    best = qw;
            }
            return best;
        }
        catch (Exception)
        {
            return adapterRam;
        }
    }

    private static bool NameMatches(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
        b.Contains(a, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ManagementObject> Query(string className)
    {
        ManagementObjectCollection results;
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}")
            {
                Options = { Timeout = TimeSpan.FromSeconds(8) },
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
