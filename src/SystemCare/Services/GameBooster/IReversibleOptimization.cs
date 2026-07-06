using SystemCare.Models;

namespace SystemCare.Services.GameBooster;

/// <summary>
/// One reversible system optimization in the Game Booster pipeline. Every change captures its prior state on
/// <see cref="ApplyAsync"/> and restores exactly that on <see cref="RevertAsync"/>. Implementations must be
/// best-effort (never throw fatally) and revert must be idempotent.
/// </summary>
public interface IReversibleOptimization
{
    /// <summary>Stable id used to match a journal record back to its optimization on revert (e.g. "power.plan").</summary>
    string Id { get; }
    OptimizationTier Tier { get; }

    /// <summary>Whether this optimization applies to the current session (e.g. notifications only if requested).</summary>
    bool IsSupported(GameSession session);

    /// <summary>Captures prior state and applies the change. Returns a journal record (with prior state).</summary>
    Task<OptimizationRecord> ApplyAsync(GameSession session, CancellationToken ct);

    /// <summary>Restores exactly what <see cref="ApplyAsync"/> captured. Must tolerate being called twice.</summary>
    Task RevertAsync(OptimizationRecord record, CancellationToken ct);
}
