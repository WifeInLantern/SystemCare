namespace SystemCare.Models;

public class Tweak
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public bool RequiresExplorerRestart { get; init; }
}
