using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.Services.GameBooster;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// The Game Booster <see cref="OptimizationEngine"/> is the safety-critical core: it must apply supported
/// optimizations in order, journal each one, revert in reverse order, keep going when a single revert throws,
/// respect the Safe/Advanced tier gate, and recover an interrupted session from the journal. These use fake
/// optimizations + an in-memory journal so no real system state is touched.
/// </summary>
public class GameBoosterEngineTests
{
    private sealed class FakeJournal : IRollbackJournal
    {
        public JournalSession? Session;
        public int ClearCount;
        public void Begin() => Session = new JournalSession();
        public void Append(OptimizationRecord record) => Session!.Records.Add(record);
        public JournalSession? Read() => Session;
        public void Clear() { Session = null; ClearCount++; }
    }

    private sealed class FakeOptimization : IReversibleOptimization
    {
        public required string Id { get; init; }
        public OptimizationTier Tier { get; init; } = OptimizationTier.Safe;
        public bool Supported { get; init; } = true;
        public bool ThrowOnRevert { get; init; }
        public required List<string> Applied { get; init; }
        public required List<string> Reverted { get; init; }

        public bool IsSupported(GameSession session) => Supported;

        public Task<OptimizationRecord> ApplyAsync(GameSession session, CancellationToken ct)
        {
            Applied.Add(Id);
            return Task.FromResult(new OptimizationRecord { Id = Id, PriorStateJson = "\"prior\"", Summary = Id });
        }

        public Task RevertAsync(OptimizationRecord record, CancellationToken ct)
        {
            if (ThrowOnRevert) throw new InvalidOperationException("revert boom");
            Reverted.Add(record.Id);
            return Task.CompletedTask;
        }
    }

    private static (OptimizationEngine Engine, FakeJournal Journal) Build(params IReversibleOptimization[] opts)
    {
        var journal = new FakeJournal();
        var engine = new OptimizationEngine(opts, journal, Substitute.For<ILogService>());
        return (engine, journal);
    }

    [Fact]
    public async Task ApplyAllAsync_AppliesInPipelineOrder_AndJournalsEach()
    {
        var applied = new List<string>();
        var reverted = new List<string>();
        // Passed out of order; the engine's fixed pipeline order puts power.plan before mem.trim.
        var (engine, journal) = Build(
            new FakeOptimization { Id = "mem.trim", Applied = applied, Reverted = reverted },
            new FakeOptimization { Id = "power.plan", Applied = applied, Reverted = reverted });

        var records = await engine.ApplyAllAsync(new GameSession(), CancellationToken.None);

        Assert.Equal(new[] { "power.plan", "mem.trim" }, applied);
        Assert.Equal(2, records.Count);
        Assert.Equal(2, journal.Session!.Records.Count);
    }

    [Fact]
    public async Task RevertAllAsync_RevertsInReverseOrder_AndClearsJournal()
    {
        var applied = new List<string>();
        var reverted = new List<string>();
        var (engine, journal) = Build(
            new FakeOptimization { Id = "power.plan", Applied = applied, Reverted = reverted },
            new FakeOptimization { Id = "mem.trim", Applied = applied, Reverted = reverted });

        await engine.ApplyAllAsync(new GameSession(), CancellationToken.None);
        await engine.RevertAllAsync(CancellationToken.None);

        Assert.Equal(new[] { "mem.trim", "power.plan" }, reverted); // reverse of applied
        Assert.Null(journal.Session);                               // journal cleared
        Assert.Equal(1, journal.ClearCount);
    }

    [Fact]
    public async Task ApplyAllAsync_SkipsAdvanced_UnlessEnabled()
    {
        var applied = new List<string>();
        var reverted = new List<string>();
        var advanced = new FakeOptimization { Id = "notify.silence", Tier = OptimizationTier.Advanced, Applied = applied, Reverted = reverted };

        var (engine, _) = Build(advanced);
        await engine.ApplyAllAsync(new GameSession { AdvancedEnabled = false }, CancellationToken.None);
        Assert.Empty(applied);

        applied.Clear();
        var (engine2, _) = Build(new FakeOptimization { Id = "notify.silence", Tier = OptimizationTier.Advanced, Applied = applied, Reverted = reverted });
        await engine2.ApplyAllAsync(new GameSession { AdvancedEnabled = true }, CancellationToken.None);
        Assert.Equal(new[] { "notify.silence" }, applied);
    }

    [Fact]
    public async Task ApplyAllAsync_SkipsUnsupportedOptimizations()
    {
        var applied = new List<string>();
        var reverted = new List<string>();
        var (engine, _) = Build(new FakeOptimization { Id = "app.suspend", Supported = false, Applied = applied, Reverted = reverted });

        var records = await engine.ApplyAllAsync(new GameSession(), CancellationToken.None);

        Assert.Empty(applied);
        Assert.Empty(records);
    }

    [Fact]
    public async Task RevertAllAsync_ContinuesWhenOneRevertThrows()
    {
        var applied = new List<string>();
        var reverted = new List<string>();
        // mem.trim is applied last → reverted first; make it throw and confirm power.plan still reverts.
        var (engine, _) = Build(
            new FakeOptimization { Id = "power.plan", Applied = applied, Reverted = reverted },
            new FakeOptimization { Id = "mem.trim", ThrowOnRevert = true, Applied = applied, Reverted = reverted });

        await engine.ApplyAllAsync(new GameSession(), CancellationToken.None);
        await engine.RevertAllAsync(CancellationToken.None); // must not throw

        Assert.Contains("power.plan", reverted);
    }

    [Fact]
    public async Task RecoverAsync_RevertsInterruptedSession_AndReturnsTrue()
    {
        var applied = new List<string>();
        var reverted = new List<string>();
        var (engine, journal) = Build(new FakeOptimization { Id = "power.plan", Applied = applied, Reverted = reverted });

        // Simulate a session that was journaled but never cleanly reverted (app crashed).
        journal.Session = new JournalSession { Records = { new OptimizationRecord { Id = "power.plan", PriorStateJson = "\"prior\"" } } };

        bool recovered = await engine.RecoverAsync(CancellationToken.None);

        Assert.True(recovered);
        Assert.Equal(new[] { "power.plan" }, reverted);
        Assert.Null(journal.Session);
    }

    [Fact]
    public async Task RecoverAsync_NoJournal_ReturnsFalse()
    {
        var (engine, _) = Build();
        Assert.False(await engine.RecoverAsync(CancellationToken.None));
    }
}
