using System.Text.Json;
using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services.GameBooster;

/// <summary>
/// Silences toast notifications during play by clearing the user's ToastEnabled flag, remembering the prior
/// value so revert restores it exactly (rather than force-enabling toasts for users who keep them off).
/// </summary>
public sealed class NotificationOptimization : IReversibleOptimization
{
    private const string PushKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string ToastValue = "ToastEnabled";

    public string Id => "notify.silence";
    public OptimizationTier Tier => OptimizationTier.Safe;
    public bool IsSupported(GameSession session) => session.SilenceNotifications;

    public Task<OptimizationRecord> ApplyAsync(GameSession session, CancellationToken ct)
    {
        int? prior = ReadToast();          // null = value was absent (default = enabled)
        bool ok = SetToasts(false);
        return Task.FromResult(new OptimizationRecord
        {
            Id = Id,
            PriorStateJson = JsonSerializer.Serialize(prior),
            Summary = ok ? "notifications silenced" : "notification toggle unavailable",
        });
    }

    public Task RevertAsync(OptimizationRecord record, CancellationToken ct)
    {
        try
        {
            var prior = JsonSerializer.Deserialize<int?>(record.PriorStateJson ?? "null");
            using var key = Registry.CurrentUser.CreateSubKey(PushKey);
            if (key is null) return Task.CompletedTask;
            if (prior is int v) key.SetValue(ToastValue, v, RegistryValueKind.DWord);
            else key.DeleteValue(ToastValue, throwOnMissingValue: false); // was absent → leave absent
        }
        catch (Exception) { }
        return Task.CompletedTask;
    }

    private static bool SetToasts(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PushKey);
            key?.SetValue(ToastValue, enabled ? 1 : 0, RegistryValueKind.DWord);
            return key is not null;
        }
        catch (Exception) { return false; }
    }

    private static int? ReadToast()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PushKey);
            return key?.GetValue(ToastValue) is int v ? v : null;
        }
        catch (Exception) { return null; }
    }
}
