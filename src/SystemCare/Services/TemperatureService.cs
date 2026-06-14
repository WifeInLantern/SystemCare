using LibreHardwareMonitor.Hardware;
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
            return results;
        }
    }

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
