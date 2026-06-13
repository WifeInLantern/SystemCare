namespace SystemCare.Models;

public class DeepCleanItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Estimated reclaimable bytes, or 0 when unknown/varies.</summary>
    public long SizeBytes { get; set; }
    /// <summary>False when the target doesn't exist on this PC (hidden from the list).</summary>
    public bool Available { get; set; } = true;
}
