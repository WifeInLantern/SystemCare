using Microsoft.Win32;

namespace SystemCare.Services;

public class GameModeResult
{
    public bool IsActive { get; init; }
    public string PowerPlanName { get; init; } = "";
    public long BytesFreed { get; init; }
    public int AppsPaused { get; init; }
    public bool NotificationsSilenced { get; init; }
}

public interface IGameModeService
{
    bool IsActive { get; }
    Task<GameModeResult> EnterAsync(IEnumerable<int> pidsToSuspend, bool silenceNotifications);
    Task<GameModeResult> ExitAsync();
}

/// <summary>
/// One-click "game/focus" state, built on top of <see cref="IBoostService"/> (High Performance power
/// plan + reversible app suspend + RAM trim) plus an optional notifications-silence toggle. Everything is
/// reversible via <see cref="ExitAsync"/>.
/// </summary>
public class GameModeService(IBoostService boost) : IGameModeService
{
    private const string PushKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    private bool _silenced;

    public bool IsActive { get; private set; }

    public async Task<GameModeResult> EnterAsync(IEnumerable<int> pidsToSuspend, bool silenceNotifications)
    {
        var b = await boost.BoostAsync(pidsToSuspend);
        _silenced = silenceNotifications && SetToasts(false);
        IsActive = true;

        return new GameModeResult
        {
            IsActive = true,
            PowerPlanName = b.PowerPlanName,
            BytesFreed = b.BytesFreed,
            AppsPaused = b.AppsPaused,
            NotificationsSilenced = _silenced,
        };
    }

    public async Task<GameModeResult> ExitAsync()
    {
        var b = await boost.RestoreAsync();
        if (_silenced) SetToasts(true);
        _silenced = false;
        IsActive = false;

        return new GameModeResult { IsActive = false, PowerPlanName = b.PowerPlanName };
    }

    /// <summary>Toggles toast notifications for the current user (reversible). Returns true if applied.</summary>
    private static bool SetToasts(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PushKey);
            key?.SetValue("ToastEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
