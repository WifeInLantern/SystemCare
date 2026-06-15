using System.Diagnostics;
using SystemCare.Native;

namespace SystemCare.Services;

public class BoostResult
{
    public bool IsBoosted { get; init; }
    public string PowerPlanName { get; init; } = "";
    public long BytesFreed { get; init; }
    public int AppsPaused { get; init; }
}

public interface IBoostService
{
    bool IsBoosted { get; }
    Task<BoostResult> BoostAsync(IEnumerable<int> pidsToSuspend);
    Task<BoostResult> RestoreAsync();
}

public class BoostService(IPowerPlanService power, IMemoryOptimizerService memory) : IBoostService
{
    private Guid? _previousScheme;
    private readonly List<int> _suspended = [];

    public bool IsBoosted { get; private set; }

    public async Task<BoostResult> BoostAsync(IEnumerable<int> pidsToSuspend)
    {
        // Switch to High Performance, remembering the current plan for Restore.
        _previousScheme ??= power.GetActiveScheme();
        power.SetActiveScheme(power.HighPerformanceGuid);

        // Pause the chosen background apps (reversible — suspend, not kill). Skip any PID we've
        // already suspended: NtSuspendProcess increments a suspend count, so suspending twice would
        // need two resumes, leaving the app frozen after a single Restore.
        int paused = 0;
        foreach (int pid in pidsToSuspend)
        {
            if (_suspended.Contains(pid)) continue;
            if (SuspendOrResume(pid, suspend: true))
            {
                _suspended.Add(pid);
                paused++;
            }
        }

        var ram = await memory.OptimizeAsync();
        IsBoosted = true;

        return new BoostResult
        {
            IsBoosted = true,
            PowerPlanName = SchemeName(power.GetActiveScheme()),
            BytesFreed = ram.BytesFreed,
            AppsPaused = paused,
        };
    }

    public Task<BoostResult> RestoreAsync() => Task.Run(() =>
    {
        foreach (int pid in _suspended) SuspendOrResume(pid, suspend: false);
        _suspended.Clear();

        if (_previousScheme is Guid prev) power.SetActiveScheme(prev);
        _previousScheme = null;
        IsBoosted = false;

        return new BoostResult { IsBoosted = false, PowerPlanName = SchemeName(power.GetActiveScheme()) };
    });

    private string SchemeName(Guid? guid) =>
        guid is null ? "" : power.ListSchemes().FirstOrDefault(s => s.Guid == guid)?.Name ?? "current plan";

    private static bool SuspendOrResume(int pid, bool suspend)
    {
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            int status = suspend ? NativeMethods.NtSuspendProcess(handle) : NativeMethods.NtResumeProcess(handle);
            return status == 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
