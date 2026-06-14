namespace SystemCare.Models;

public class HardwareSpec
{
    public required string Category { get; init; }
    public required string Name { get; init; }
    public string Detail { get; init; } = "";
    public string Icon { get; init; } = "Info24";
}

public class HardwareReport
{
    public List<HardwareSpec> Specs { get; } = [];
}

/// <summary>A live temperature reading for one component, matched to a <see cref="HardwareSpec"/>
/// by <see cref="Category"/> (and <see cref="HardwareName"/> when several share a category).</summary>
public record ComponentTemperature(string Category, string HardwareName, double Celsius);
