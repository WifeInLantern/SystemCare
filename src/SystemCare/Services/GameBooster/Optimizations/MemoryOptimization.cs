using SystemCare.Models;

namespace SystemCare.Services.GameBooster;

/// <summary>
/// Trims working sets to reclaim unused RAM (reuses the existing IMemoryOptimizerService). This is
/// self-healing — pages fault back on demand — so there is nothing to revert. Phase 1 will pass the game's
/// protected PIDs through so the game itself is never trimmed.
/// </summary>
public sealed class MemoryOptimization : IReversibleOptimization
{
    private readonly IMemoryOptimizerService _memory;
    public MemoryOptimization(IMemoryOptimizerService memory) => _memory = memory;

    public string Id => "mem.trim";
    public OptimizationTier Tier => OptimizationTier.Safe;
    public bool IsSupported(GameSession session) => true;

    public async Task<OptimizationRecord> ApplyAsync(GameSession session, CancellationToken ct)
    {
        var result = await _memory.OptimizeAsync();
        return new OptimizationRecord
        {
            Id = Id,
            PriorStateJson = null, // nothing to restore — trimming is naturally reversible by the OS
            Summary = $"freed {result.BytesFreed / (1024 * 1024)} MB across {result.ProcessesTrimmed} process(es)",
            BytesFreed = result.BytesFreed,
            Count = result.ProcessesTrimmed,
        };
    }

    public Task RevertAsync(OptimizationRecord record, CancellationToken ct) => Task.CompletedTask;
}
