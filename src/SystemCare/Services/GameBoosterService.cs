using SystemCare.Models;
using SystemCare.Services.GameBooster;

namespace SystemCare.Services;

public interface IGameBoosterService
{
    bool IsActive { get; }
    Task<GameBoosterResult> ActivateAsync(IEnumerable<int> pidsToSuspend, bool silenceNotifications);
    Task<GameBoosterResult> DeactivateAsync();
    /// <summary>Called at app startup: restores the system if a previous session didn't close cleanly.</summary>
    Task RecoverIfInterruptedAsync();
}

/// <summary>
/// Premium "Game Booster" state, built on the reversible <see cref="IOptimizationEngine"/>. Phase 0 wraps the
/// original Game Mode behaviour (High Performance power plan, reversible app suspend, RAM trim, optional
/// notification silence) as journaled optimizations, so a crash mid-session is still fully restored on next launch.
/// </summary>
public sealed class GameBoosterService : IGameBoosterService
{
    private readonly IOptimizationEngine _engine;
    private readonly IPowerPlanService _power;

    public bool IsActive { get; private set; }

    public GameBoosterService(IOptimizationEngine engine, IPowerPlanService power)
    {
        _engine = engine;
        _power = power;
    }

    public async Task<GameBoosterResult> ActivateAsync(IEnumerable<int> pidsToSuspend, bool silenceNotifications)
    {
        var session = new GameSession
        {
            SelectedPids = pidsToSuspend.ToList(),
            SilenceNotifications = silenceNotifications,
            AdvancedEnabled = false,          // Phase 0: Safe tier only
            OnAcPower = true,
        };

        var records = await _engine.ApplyAllAsync(session, CancellationToken.None);
        IsActive = true;

        var power = records.FirstOrDefault(r => r.Id == "power.plan");
        return new GameBoosterResult
        {
            IsActive = true,
            PowerPlanName = power?.Summary ?? SchemeName(_power.GetActiveScheme()),
            BytesFreed = records.Sum(r => r.BytesFreed),
            AppsPaused = records.Where(r => r.Id == "app.suspend").Sum(r => r.Count),
            NotificationsSilenced = records.Any(r => r.Id == "notify.silence"),
        };
    }

    public async Task<GameBoosterResult> DeactivateAsync()
    {
        await _engine.RevertAllAsync(CancellationToken.None);
        IsActive = false;
        return new GameBoosterResult { IsActive = false, PowerPlanName = SchemeName(_power.GetActiveScheme()) };
    }

    public async Task RecoverIfInterruptedAsync()
    {
        try { await _engine.RecoverAsync(CancellationToken.None); }
        catch (Exception) { /* startup must never fail over recovery */ }
    }

    private string SchemeName(Guid? guid) =>
        guid is null ? "current plan" : _power.ListSchemes().FirstOrDefault(s => s.Guid == guid)?.Name ?? "current plan";
}
