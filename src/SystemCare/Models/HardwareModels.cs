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
