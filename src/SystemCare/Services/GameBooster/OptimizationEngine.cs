using SystemCare.Models;

namespace SystemCare.Services.GameBooster;

public interface IOptimizationEngine
{
    /// <summary>Runs the pipeline in order, journaling each applied optimization. Returns the applied records.</summary>
    Task<IReadOnlyList<OptimizationRecord>> ApplyAllAsync(GameSession session, CancellationToken ct);
    /// <summary>Reverts the current journaled session in reverse order, then clears the journal.</summary>
    Task RevertAllAsync(CancellationToken ct);
    /// <summary>At startup: if a journal from an interrupted session exists, revert it. Returns true if it did.</summary>
    Task<bool> RecoverAsync(CancellationToken ct);
}

/// <summary>
/// Ordered, journaled optimization runner. Applies each supported optimization in a fixed order (cheap/safe
/// first), reverts in reverse. Every step is best-effort: a failure logs and continues, and never blocks the
/// revert of the others.
/// </summary>
public sealed class OptimizationEngine : IOptimizationEngine
{
    // Apply order; revert runs the reverse. Ids not listed here fall to the end.
    private static readonly string[] Order = ["power.plan", "app.suspend", "mem.trim", "notify.silence"];

    private readonly IReadOnlyList<IReversibleOptimization> _pipeline;
    private readonly Dictionary<string, IReversibleOptimization> _byId;
    private readonly IRollbackJournal _journal;
    private readonly ILogService _log;

    public OptimizationEngine(IEnumerable<IReversibleOptimization> optimizations, IRollbackJournal journal, ILogService log)
    {
        _pipeline = optimizations
            .OrderBy(o => Array.IndexOf(Order, o.Id) is var i && i >= 0 ? i : int.MaxValue)
            .ToList();
        _byId = _pipeline.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        _journal = journal;
        _log = log;
    }

    public async Task<IReadOnlyList<OptimizationRecord>> ApplyAllAsync(GameSession session, CancellationToken ct)
    {
        _journal.Begin();
        var applied = new List<OptimizationRecord>();

        foreach (var opt in _pipeline)
        {
            if (opt.Tier == OptimizationTier.Advanced && !session.AdvancedEnabled) continue;
            if (!opt.IsSupported(session)) { _log.Info("GameBooster", $"{opt.Id}: skipped (not applicable)"); continue; }

            try
            {
                var record = await opt.ApplyAsync(session, ct);
                _journal.Append(record);
                applied.Add(record);
                _log.Info("GameBooster", $"{opt.Id}: applied — {record.Summary}");
            }
            catch (Exception ex)
            {
                _log.Warn("GameBooster", $"{opt.Id}: apply failed — {ex.Message}");
            }
        }

        return applied;
    }

    public async Task RevertAllAsync(CancellationToken ct)
    {
        var session = _journal.Read();
        if (session is null) return;

        // Reverse order so later changes unwind before earlier ones.
        for (int i = session.Records.Count - 1; i >= 0; i--)
        {
            var record = session.Records[i];
            if (!_byId.TryGetValue(record.Id, out var opt))
            {
                _log.Warn("GameBooster", $"{record.Id}: no optimization to revert with (skipped)");
                continue;
            }

            try { await opt.RevertAsync(record, ct); _log.Info("GameBooster", $"{record.Id}: reverted"); }
            catch (Exception ex) { _log.Warn("GameBooster", $"{record.Id}: revert failed — {ex.Message}"); }
        }

        _journal.Clear();
    }

    public async Task<bool> RecoverAsync(CancellationToken ct)
    {
        if (_journal.Read() is null) return false;
        _log.Info("GameBooster", "Found an interrupted session — restoring system changes.");
        await RevertAllAsync(ct);
        return true;
    }
}
