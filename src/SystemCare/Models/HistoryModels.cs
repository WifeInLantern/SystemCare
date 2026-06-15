namespace SystemCare.Models;

/// <summary>One recorded maintenance action, persisted to %AppData%\SystemCare\history.json.</summary>
public class HistoryEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = "";
    public string Summary { get; set; } = "";
    public long BytesFreed { get; set; }
    public int ItemCount { get; set; }
    public string Icon { get; set; } = "History24";
}
