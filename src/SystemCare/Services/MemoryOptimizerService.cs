using System.Diagnostics;
using SystemCare.Native;

namespace SystemCare.Services;

public class MemoryOptimizeResult
{
    public long BytesFreed { get; init; }
    public int ProcessesTrimmed { get; init; }
}

public interface IMemoryOptimizerService
{
    Task<MemoryOptimizeResult> OptimizeAsync();
}

public class MemoryOptimizerService : IMemoryOptimizerService
{
    // System pseudo-processes that must never be touched.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Memory Compression", "Secure System", "Registry", "Idle", "System",
    };

    public Task<MemoryOptimizeResult> OptimizeAsync() => Task.Run(() =>
    {
        long freed = 0;
        int trimmed = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4 || ProtectedNames.Contains(process.ProcessName)) continue;

                    long before = process.WorkingSet64;
                    IntPtr handle = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_SET_QUOTA,
                        false, process.Id);
                    if (handle == IntPtr.Zero) continue;

                    try
                    {
                        if (!NativeMethods.EmptyWorkingSet(handle)) continue;
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(handle);
                    }

                    process.Refresh();
                    long delta = before - process.WorkingSet64;
                    if (delta > 0) freed += delta;
                    trimmed++;
                }
                catch (Exception)
                {
                    // protected/exited process — skip
                }
            }
        }

        return new MemoryOptimizeResult { BytesFreed = freed, ProcessesTrimmed = trimmed };
    });
}
