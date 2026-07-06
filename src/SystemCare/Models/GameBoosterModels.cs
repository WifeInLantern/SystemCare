namespace SystemCare.Models;

/// <summary>Safe optimizations run by default; Advanced ones only when the user opts in.</summary>
public enum OptimizationTier { Safe, Advanced }

/// <summary>
/// Context passed to every optimization when Game Booster activates. In Phase 0 this is populated from the
/// user's manual selection; later phases fill <see cref="ProtectedPids"/> from the detected game's process tree.
/// </summary>
public sealed class GameSession
{
    public IReadOnlyList<int> SelectedPids { get; init; } = [];
    /// <summary>PIDs that must never be trimmed/suspended/deprioritized (the game + its children).</summary>
    public HashSet<int> ProtectedPids { get; init; } = [];
    public bool SilenceNotifications { get; init; }
    public bool AdvancedEnabled { get; init; }
    public bool OnAcPower { get; init; } = true;
}

/// <summary>
/// The rollback token an optimization produces when it applies. <see cref="PriorStateJson"/> is the only field
/// needed to revert; the rest are for the UI/result summary. Serialized to the session journal so a crash
/// mid-session can still be undone on next launch.
/// </summary>
public sealed class OptimizationRecord
{
    public required string Id { get; init; }
    /// <summary>JSON-serialized prior state, owned and interpreted by the optimization that created it.</summary>
    public string? PriorStateJson { get; init; }
    public string Summary { get; init; } = "";
    public long BytesFreed { get; init; }
    public int Count { get; init; }
}

/// <summary>Aggregated result surfaced to the Game Booster UI after activate/deactivate.</summary>
public sealed class GameBoosterResult
{
    public bool IsActive { get; init; }
    public string PowerPlanName { get; init; } = "";
    public long BytesFreed { get; init; }
    public int AppsPaused { get; init; }
    public bool NotificationsSilenced { get; init; }
}
