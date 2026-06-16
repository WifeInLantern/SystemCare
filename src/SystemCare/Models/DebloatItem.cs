namespace SystemCare.Models;

/// <summary>
/// One curated debloat action shown as a checkbox. The actual apply/revert logic lives in
/// <c>DebloatService</c>, keyed by <see cref="Id"/> — items only carry display metadata, so the
/// feature can only ever act on this vetted allowlist (never arbitrary user input).
/// </summary>
public class DebloatItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Group { get; init; }
    /// <summary>Pre-checked in the UI when true (low-risk, widely recommended).</summary>
    public bool Recommended { get; init; }
    /// <summary>False for permanent actions (app removal) that have no Revert.</summary>
    public bool Reversible { get; init; } = true;
    /// <summary>Optional caution shown next to the item.</summary>
    public string? Warning { get; init; }
}

public class DebloatResult
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public bool RestorePointMade { get; set; }
    public string Message { get; set; } = "";
}
