using System.Management;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ITemperatureService : IDisposable
{
    /// <summary>
    /// Reads a representative temperature (°C) for each sensor-bearing component (CPU, GPU, drives,
    /// motherboard). Lazily opens the sensor backend on first call; returns an empty list if sensors
    /// are unavailable (e.g. the kernel driver is blocked by HVCI/Memory Integrity). Never throws.
    /// </summary>
    IReadOnlyList<ComponentTemperature> Read();

    /// <summary>
    /// Reads <b>all</b> live sensors (temperatures, fans, voltages, clocks, loads, power) across every
    /// component, for the Sensors hub. Shares the same backend as <see cref="Read"/>. Never throws.
    /// </summary>
    IReadOnlyList<SensorReading> ReadSensors();
}

/// <summary>
/// Hardware temperatures via LibreHardwareMonitorLib. A single <see cref="Computer"/> is opened once
/// and updated on each <see cref="Read"/>. Reading touches a kernel driver (CPU/motherboard) and SMART
/// (drives), so callers should poll off the UI thread.
/// </summary>
public sealed class TemperatureService : ITemperatureService
{
    private readonly object _gate = new();
    private readonly UpdateVisitor _visitor = new();
    private Computer? _computer;
    private bool _initFailed;

    public IReadOnlyList<ComponentTemperature> Read()
    {
        lock (_gate)
        {
            var computer = EnsureOpen();
            if (computer is null) return [];

            var results = new List<ComponentTemperature>();
            try
            {
                computer.Accept(_visitor); // refresh every hardware + subhardware
                foreach (var hw in computer.Hardware)
                {
                    string? category = CategoryFor(hw.HardwareType);
                    if (category is null) continue;
                    if (PickTemperature(hw) is double celsius)
                        results.Add(new ComponentTemperature(category, hw.Name, celsius));
                }
            }
            catch (Exception)
            {
                // A failed update shouldn't take the page down — just report what we have.
            }

            // 2.16.x fix: on many systems (e.g. Ryzen with Memory Integrity/HVCI blocking the
            // WinRing0 kernel driver) LHM reports no CPU temperature at all. Fall back to the
            // ACPI thermal zone via WMI — approximate, but honest and better than a blank tile.
            if (!results.Any(r => r.Category == "Processor") && ReadAcpiCpuTemperature() is double acpi)
                results.Add(new ComponentTemperature("Processor", "ACPI thermal zone (approx.)", acpi));

            return results;
        }
    }

    public IReadOnlyList<SensorReading> ReadSensors()
    {
        lock (_gate)
        {
            var computer = EnsureOpen();
            if (computer is null) return [];

            var results = new List<SensorReading>();
            try
            {
                computer.Accept(_visitor);
                foreach (var hw in computer.Hardware)
                {
                    string? category = CategoryFor(hw.HardwareType);
                    if (category is null) continue;
                    CollectSensors(hw, hw.Name, category, results);
                }
            }
            catch (Exception)
            {
                // a failed update shouldn't take the page down — return what we have
            }

            // Same ACPI fallback as Read(): gives the Sensors hub's CPU TEMP tile a real value
            // when the kernel-driver path is blocked (see Read() for the why).
            if (!results.Any(r => r.Category == "Processor" && r.Kind == SensorKind.Temperature)
                && ReadAcpiCpuTemperature() is double acpi)
            {
                results.Add(new SensorReading("ACPI thermal zone", "Processor", SensorKind.Temperature,
                    "CPU (ACPI, approx.)", acpi));
            }

            return results;
        }
    }

    // --- ACPI fallback (2.16.x) -------------------------------------------------------------

    private double? _acpiCache;
    private DateTime _acpiCacheUtc;

    /// <summary>MSAcpi_ThermalZoneTemperature (root\WMI), decikelvin → °C. Cached for 5s because
    /// callers poll at 1s and WMI round-trips are comparatively expensive. Null when unavailable
    /// or implausible (many boards simply don't expose a live thermal zone).</summary>
    private double? ReadAcpiCpuTemperature()
    {
        if (DateTime.UtcNow - _acpiCacheUtc < TimeSpan.FromSeconds(5)) return _acpiCache;
        _acpiCacheUtc = DateTime.UtcNow;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var rows = searcher.Get();
            double best = 0;
            foreach (var row in rows)
            {
                if (row["CurrentTemperature"] is null) continue;
                double celsius = Convert.ToDouble(row["CurrentTemperature"]) / 10.0 - 273.15;
                if (celsius > best) best = celsius;
            }
            _acpiCache = best is > 5 and < 110 ? Math.Round(best) : null;
        }
        catch (Exception)
        {
            _acpiCache = null; // WMI class missing/access denied — normal on many systems
        }
        return _acpiCache;
    }

    /// <summary>True when Windows Memory Integrity (HVCI / Core Isolation) is enabled — the usual
    /// reason the sensor kernel driver can't load and CPU temperature goes missing.</summary>
    public static bool IsMemoryIntegrityOn()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            return key?.GetValue("Enabled") is int v && v == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void CollectSensors(IHardware hw, string component, string category, List<SensorReading> into)
    {
        foreach (var s in hw.Sensors)
        {
            if (MapKind(s.SensorType) is not SensorKind kind) continue;
            if (s.Value is not float v || float.IsNaN(v) || float.IsInfinity(v)) continue;
            if (kind == SensorKind.Temperature &&
                (v <= 0 || v >= 200 || ThresholdNames.Any(t => s.Name.Contains(t, StringComparison.OrdinalIgnoreCase))))
                continue; // skip fixed limits / bogus temps, matching PickTemperature
            into.Add(new SensorReading(component, category, kind, s.Name, Math.Round((double)v, 2)));
        }
        foreach (var sub in hw.SubHardware) CollectSensors(sub, component, category, into);
    }

    private static SensorKind? MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Load => SensorKind.Load,
        SensorType.Power => SensorKind.Power,
        SensorType.Control => SensorKind.Control,
        _ => null,
    };

    private Computer? EnsureOpen()
    {
        if (_computer is not null) return _computer;
        if (_initFailed) return null;
        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true,
            };
            computer.Open();
            _computer = computer;
            return computer;
        }
        catch (Exception)
        {
            _initFailed = true; // don't keep retrying a backend that can't load
            return null;
        }
    }

    private static string? CategoryFor(HardwareType type) => type switch
    {
        HardwareType.Cpu => "Processor",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "Graphics",
        HardwareType.Motherboard => "Motherboard",
        HardwareType.Storage => "Disk",
        _ => null,
    };

    private static double? PickTemperature(IHardware hw)
    {
        var sensors = CollectTempSensors(hw);
        if (sensors.Count == 0) return null;

        // Names that best represent each component's "the" temperature.
        string[] prefer = hw.HardwareType switch
        {
            HardwareType.Cpu => ["package", "tctl", "tdie", "core average", "core max"],
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ["gpu core", "core"],
            HardwareType.Storage => ["temperature"],
            HardwareType.Motherboard => ["system", "mainboard", "motherboard", "board"],
            _ => [],
        };

        foreach (var key in prefer)
        {
            var hit = sensors.FirstOrDefault(s => s.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return Round(hit);
        }

        // CPU/GPU: the hottest reading is the safe representative; board/disk: the first sensor
        // (avoids surfacing a VRM/hot-spot spike as the component temperature).
        bool isProcessorLike = hw.HardwareType is HardwareType.Cpu
            or HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        var chosen = isProcessorLike ? sensors.OrderByDescending(s => s.Value).First() : sensors[0];
        return Round(chosen);
    }

    // Sensors that report a fixed limit (e.g. NVMe "Warning/Critical Temperature"), not a live reading.
    private static readonly string[] ThresholdNames = ["warning", "critical", "limit", "tjmax"];

    private static List<ISensor> CollectTempSensors(IHardware hw)
    {
        var list = new List<ISensor>();
        void Walk(IHardware h)
        {
            foreach (var s in h.Sensors)
                if (s.SensorType == SensorType.Temperature && s.Value is float v && !float.IsNaN(v) && v > 0 && v < 200
                    && !ThresholdNames.Any(t => s.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    list.Add(s);
            foreach (var sub in h.SubHardware) Walk(sub);
        }
        Walk(hw);
        return list;
    }

    private static double Round(ISensor s) => Math.Round((double)(s.Value ?? 0));

    public void Dispose()
    {
        lock (_gate)
        {
            try { _computer?.Close(); } catch (Exception) { }
            _computer = null;
        }
    }

    /// <summary>Updates each hardware (and its subhardware) so sensor values are current before reading.</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
