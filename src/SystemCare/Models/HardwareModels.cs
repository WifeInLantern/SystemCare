namespace SystemCare.Models;

/// <summary>Logical grouping of a <see cref="HardwareSpec"/> into a panel section.</summary>
public enum HardwareSection { Cpu, Gpu, Ram, Storage, Os, Network, Board, Battery }

public class HardwareSpec
{
    /// <summary>Coarse category used to match a live temperature (Processor/Graphics/Disk/Motherboard).</summary>
    public required string Category { get; init; }
    public required string Name { get; init; }
    public string Detail { get; init; } = "";
    public string Icon { get; init; } = "Info24";
    public HardwareSection Section { get; init; } = HardwareSection.Os;
    /// <summary>Optional richer text shown on hover.</summary>
    public string? Tooltip { get; init; }
    /// <summary>Optional health indicator, e.g. "Healthy", "Pred Fail", "92%".</summary>
    public string? Health { get; init; }
    public string? DriverVersion { get; init; }
}

public class HardwareReport
{
    public List<HardwareSpec> Specs { get; } = [];
}

/// <summary>A live temperature reading for one component, matched to a <see cref="HardwareSpec"/>
/// by <see cref="Category"/> (and <see cref="HardwareName"/> when several share a category).</summary>
public record ComponentTemperature(string Category, string HardwareName, double Celsius);
