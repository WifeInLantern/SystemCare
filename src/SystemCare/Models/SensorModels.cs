namespace SystemCare.Models;

/// <summary>The kind of a hardware sensor reading, which also determines its display unit.</summary>
public enum SensorKind
{
    Temperature,
    Fan,
    Voltage,
    Clock,
    Load,
    Power,
    Control,
}

/// <summary>
/// One live hardware sensor reading. <see cref="Component"/> is the device name (e.g. the CPU/GPU model),
/// <see cref="Category"/> the coarse group (Processor/Graphics/Motherboard/Disk), <see cref="Name"/> the
/// sensor's own label (e.g. "Core #1", "GPU Fan").
/// </summary>
public record SensorReading(string Component, string Category, SensorKind Kind, string Name, double Value);
