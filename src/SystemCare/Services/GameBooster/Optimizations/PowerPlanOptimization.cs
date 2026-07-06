using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services.GameBooster;

/// <summary>Switches to the High Performance power plan, remembering the prior scheme for revert.</summary>
public sealed class PowerPlanOptimization : IReversibleOptimization
{
    private readonly IPowerPlanService _power;
    public PowerPlanOptimization(IPowerPlanService power) => _power = power;

    public string Id => "power.plan";
    public OptimizationTier Tier => OptimizationTier.Safe;
    public bool IsSupported(GameSession session) => true;

    public Task<OptimizationRecord> ApplyAsync(GameSession session, CancellationToken ct)
    {
        Guid? prior = _power.GetActiveScheme();
        _power.SetActiveScheme(_power.HighPerformanceGuid);
        string name = _power.ListSchemes().FirstOrDefault(s => s.Guid == _power.HighPerformanceGuid)?.Name
                      ?? "High Performance";

        return Task.FromResult(new OptimizationRecord
        {
            Id = Id,
            PriorStateJson = JsonSerializer.Serialize(prior),
            Summary = name,
        });
    }

    public Task RevertAsync(OptimizationRecord record, CancellationToken ct)
    {
        try
        {
            var prior = JsonSerializer.Deserialize<Guid?>(record.PriorStateJson ?? "null");
            if (prior is Guid g) _power.SetActiveScheme(g);
        }
        catch (Exception) { }
        return Task.CompletedTask;
    }
}
