using System.Diagnostics;
using System.Management;
using System.Text;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IDiskMaintenanceService
{
    /// <summary>Reads per-physical-disk SMART/health via the modern Storage WMI provider.</summary>
    Task<List<PhysicalDiskHealth>> GetPhysicalDisksAsync();

    /// <summary>
    /// Launches a Windows maintenance tool and streams its output line by line.
    /// <paramref name="onOutput"/> is invoked from a background thread. Cancellation kills the process.
    /// </summary>
    Task<int> RunAsync(string fileName, string arguments, Action<string> onOutput, Encoding? encoding, CancellationToken ct);
}

public class DiskMaintenanceService(ITemperatureService temperatures) : IDiskMaintenanceService
{
    public Task<List<PhysicalDiskHealth>> GetPhysicalDisksAsync() => Task.Run(() =>
    {
        var disks = new List<PhysicalDiskHealth>();

        // Preferred: MSFT_PhysicalDisk (root\Microsoft\Windows\Storage) gives HealthStatus + MediaType,
        // enriched per-disk with its associated MSFT_StorageReliabilityCounter (wear/temp/hours/errors).
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT FriendlyName, MediaType, HealthStatus, Size FROM MSFT_PhysicalDisk"));
            foreach (ManagementBaseObject baseMo in searcher.Get())
            {
                var mo = (ManagementObject)baseMo;
                var r = ReadReliability(mo);
                disks.Add(new PhysicalDiskHealth
                {
                    Name = mo["FriendlyName"]?.ToString()?.Trim() ?? "Disk",
                    SizeBytes = ToLong(mo["Size"]),
                    MediaType = MapMedia(ToInt(mo["MediaType"])),
                    Health = MapHealth(ToInt(mo["HealthStatus"])),
                    WearPercent = r.Wear,
                    TemperatureC = r.TemperatureC,
                    PowerOnHours = r.PowerOnHours,
                    ReadErrors = r.ReadErrors,
                    WriteErrors = r.WriteErrors,
                    ReallocatedSectors = r.Uncorrectable,
                });
            }
        }
        catch (Exception)
        {
            // ignore; fall back below
        }

        // Fallback: legacy Win32_DiskDrive.Status ("OK" / "Pred Fail" / ...).
        if (disks.Count == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model, Size, Status FROM Win32_DiskDrive");
                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    string status = mo["Status"]?.ToString() ?? "";
                    disks.Add(new PhysicalDiskHealth
                    {
                        Name = mo["Model"]?.ToString()?.Trim() ?? "Disk",
                        SizeBytes = ToLong(mo["Size"]),
                        MediaType = "Disk",
                        Health = status.Equals("OK", StringComparison.OrdinalIgnoreCase)
                            ? DiskHealthStatus.Healthy
                            : string.IsNullOrEmpty(status) ? DiskHealthStatus.Unknown : DiskHealthStatus.Warning,
                    });
                }
            }
            catch (Exception) { }
        }

        MergeTemperatures(disks);
        return disks;
    });

    /// <summary>Reads the disk's associated reliability counters; all fields null when unavailable.</summary>
    private static (int? Wear, double? TemperatureC, long? PowerOnHours, long? ReadErrors, long? WriteErrors, long? Uncorrectable)
        ReadReliability(ManagementObject disk)
    {
        try
        {
            foreach (ManagementBaseObject rel in disk.GetRelated("MSFT_StorageReliabilityCounter"))
            {
                using (rel)
                {
                    long? uncorr = AddN(ToLongN(rel["ReadErrorsUncorrected"]), ToLongN(rel["WriteErrorsUncorrected"]));
                    return (
                        ToIntN(rel["Wear"]),
                        ToDoubleN(rel["Temperature"]),
                        ToLongN(rel["PowerOnHours"]),
                        ToLongN(rel["ReadErrorsTotal"]),
                        ToLongN(rel["WriteErrorsTotal"]),
                        uncorr);
                }
            }
        }
        catch (Exception) { }
        return (null, null, null, null, null, null);
    }

    /// <summary>Fills in any missing per-disk temperature from the sensor backend (LibreHardwareMonitor).</summary>
    private void MergeTemperatures(List<PhysicalDiskHealth> disks)
    {
        if (disks.Count == 0 || disks.All(d => d.TemperatureC is > 0)) return;
        List<ComponentTemperature> temps;
        try { temps = temperatures.Read().Where(t => t.Category == "Disk").ToList(); }
        catch (Exception) { return; }
        if (temps.Count == 0) return;

        foreach (var disk in disks)
        {
            if (disk.TemperatureC is > 0) continue;
            var match = temps.Count == 1
                ? temps[0]
                : temps.OrderByDescending(t => TokenOverlap(disk.Name, t.HardwareName)).FirstOrDefault();
            if (match is not null) disk.TemperatureC = match.Celsius;
        }
    }

    private static int TokenOverlap(string a, string b)
    {
        var tb = b.ToLowerInvariant().Split(' ', '-', '_').Where(t => t.Length >= 2).ToHashSet();
        return a.ToLowerInvariant().Split(' ', '-', '_').Count(t => t.Length >= 2 && tb.Contains(t));
    }

    public async Task<int> RunAsync(string fileName, string arguments, Action<string> onOutput, Encoding? encoding, CancellationToken ct)
    {
        // Launch through cmd.exe. chkdsk/sfc attach to the console in a way that makes a direct
        // CreateProcess with redirected stdout fail with "Access is denied" even when elevated;
        // wrapping in `cmd /c` avoids that and lets cmd resolve the tool from PATH (System32).
        var psi = new ProcessStartInfo("cmd.exe", $"/c {fileName} {arguments}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory,
        };
        if (encoding is not null)
        {
            psi.StandardOutputEncoding = encoding;
            psi.StandardErrorEncoding = encoding;
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            onOutput($"Could not start {fileName}: {ex.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var reg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        return process.ExitCode;
    }

    private static string MapMedia(int mediaType) => mediaType switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Disk",
    };

    private static DiskHealthStatus MapHealth(int health) => health switch
    {
        0 => DiskHealthStatus.Healthy,
        1 => DiskHealthStatus.Warning,
        2 => DiskHealthStatus.Unhealthy,
        _ => DiskHealthStatus.Unknown,
    };

    private static long ToLong(object? o)
    {
        try { return o is null ? 0 : Convert.ToInt64(o); } catch (Exception) { return 0; }
    }

    private static int ToInt(object? o)
    {
        try { return o is null ? -1 : Convert.ToInt32(o); } catch (Exception) { return -1; }
    }

    private static int? ToIntN(object? o)
    {
        try { return o is null ? null : Convert.ToInt32(o); } catch (Exception) { return null; }
    }

    private static long? ToLongN(object? o)
    {
        try { return o is null ? null : Convert.ToInt64(o); } catch (Exception) { return null; }
    }

    private static double? ToDoubleN(object? o)
    {
        try { return o is null ? null : Convert.ToDouble(o); } catch (Exception) { return null; }
    }

    private static long? AddN(long? a, long? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
}
