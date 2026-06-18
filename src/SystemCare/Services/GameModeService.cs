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
    private const string ToastValue = "ToastEnabled";
    private bool _silenced;
    private int? _previousToast; // the user's ToastEnabled before we silenced (null = value was absent)

    public bool IsActive { get; private set; }

    public async Task<GameModeResult> EnterAsync(IEnumerable<int> pidsToSuspend, bool silenceNotifications)
    {
        var b = await boost.BoostAsync(pidsToSuspend);

        _silenced = false;
        if (silenceNotifications)
        {
            // Remember the user's current preference so Exit restores it exactly, instead of
            // force-enabling toasts (which would clobber the setting of users who keep them off).
            _previousToast = ReadToast();
            _silenced = SetToasts(false);
        }
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
        if (_silenced) RestoreToasts();
        _silenced = false;
        IsActive = false;

        return new GameModeResult { IsActive = false, PowerPlanName = b.PowerPlanName };
    }

    /// <summary>Disables toast notifications for the current user. Returns true if applied.</summary>
    private static bool SetToasts(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PushKey);
            key?.SetValue(ToastValue, enabled ? 1 : 0, RegistryValueKind.DWord);
            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Reads the current ToastEnabled value; null if absent or not a DWORD.</summary>
    private static int? ReadToast()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PushKey);
            return key?.GetValue(ToastValue) is int v ? v : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Restores the user's notification preference captured at Enter (re-enables, or removes the value if it was absent).</summary>
    private void RestoreToasts()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PushKey);
            if (key is null) return;
            if (_previousToast is int v) key.SetValue(ToastValue, v, RegistryValueKind.DWord);
            else key.DeleteValue(ToastValue, throwOnMissingValue: false); // was absent → leave it absent (default = enabled)
        }
        catch (Exception) { }
        finally { _previousToast = null; }
    }
}
