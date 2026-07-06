using System.Text.Json;
using SystemCare.Models;
using SystemCare.Native;

namespace SystemCare.Services.GameBooster;

/// <summary>
/// Suspends the selected background apps (reversible freeze via NtSuspendProcess — never a kill). Records the
/// PIDs it actually suspended so revert resumes exactly those. Skips any protected PID (the game + children).
/// </summary>
public sealed class AppSuspendOptimization : IReversibleOptimization
{
    public string Id => "app.suspend";
    public OptimizationTier Tier => OptimizationTier.Safe;
    public bool IsSupported(GameSession session) => session.SelectedPids.Count > 0;

    public Task<OptimizationRecord> ApplyAsync(GameSession session, CancellationToken ct) => Task.Run(() =>
    {
        var suspended = new List<int>();
        foreach (int pid in session.SelectedPids.Distinct())
        {
            if (session.ProtectedPids.Contains(pid)) continue;
            if (SuspendOrResume(pid, suspend: true)) suspended.Add(pid);
        }

        return new OptimizationRecord
        {
            Id = Id,
            PriorStateJson = JsonSerializer.Serialize(suspended),
            Summary = $"{suspended.Count} app(s) paused",
            Count = suspended.Count,
        };
    }, ct);

    public Task RevertAsync(OptimizationRecord record, CancellationToken ct) => Task.Run(() =>
    {
        var pids = JsonSerializer.Deserialize<List<int>>(record.PriorStateJson ?? "[]") ?? [];
        foreach (int pid in pids) SuspendOrResume(pid, suspend: false);
    }, ct);

    // NtSuspendProcess increments a suspend count; the caller (engine) applies once and reverts once, keeping it balanced.
    private static bool SuspendOrResume(int pid, bool suspend)
    {
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            int status = suspend ? NativeMethods.NtSuspendProcess(handle) : NativeMethods.NtResumeProcess(handle);
            return status == 0;
        }
        catch (Exception) { return false; }
        finally { NativeMethods.CloseHandle(handle); }
    }
}
