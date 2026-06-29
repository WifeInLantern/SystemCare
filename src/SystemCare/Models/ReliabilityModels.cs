namespace SystemCare.Models;

/// <summary>The kind of reliability problem recorded in the Windows Event Log.</summary>
public enum ReliabilityCategory
{
    BlueScreen,
    UnexpectedShutdown,
    Crash,
    AppHang,
    DiskError,
    ServiceFailure,
}

public enum ReliabilitySeverity { Warning, Error, Critical }

/// <summary>One classified reliability event read from the Event Log.</summary>
public record ReliabilityEvent(
    ReliabilityCategory Category,
    ReliabilitySeverity Severity,
    string Source,
    string Title,
    DateTime TimeUtc);

/// <summary>The result of a reliability scan: the classified events plus a 0–100 stability score.</summary>
public class ReliabilityReport
{
    public List<ReliabilityEvent> Events { get; init; } = [];
    public int Score { get; init; }
    public int DaysAnalyzed { get; init; }
    /// <summary>False when the Event Log couldn't be read at all (access denied / service off).</summary>
    public bool Read { get; init; } = true;
}
