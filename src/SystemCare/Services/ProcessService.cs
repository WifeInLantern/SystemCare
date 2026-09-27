using System.Diagnostics;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IProcessService
{
    /// <summary>Snapshot of user-visible processes with RAM and CPU% (CPU needs two calls to populate).</summary>
    List<ProcessEntry> GetProcesses();
    bool EndProcess(int pid);
}

public class ProcessService : IProcessService
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _last = new();

    public List<ProcessEntry> GetProcesses()
    {
        var entries = new List<ProcessEntry>();
        var seen = new HashSet<int>();
        int cores = Math.Max(1, Environment.ProcessorCount);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4) continue;
                    seen.Add(process.Id);

                    double cpu = 0;
                    try
                    {
                        var now = DateTime.UtcNow;
                        var total = process.TotalProcessorTime;
                        if (_last.TryGetValue(process.Id, out var prev))
                        {
                            double wall = (now - prev.At).TotalMilliseconds;
                            if (wall > 0)
                                cpu = Math.Clamp((total - prev.Cpu).TotalMilliseconds / (wall * cores) * 100, 0, 100);
                        }
                        _last[process.Id] = (total, now);
                    }
                    catch (Exception)
                    {
                        // access denied for CPU time on protected processes — leave 0
                    }

                    entries.Add(new ProcessEntry
                    {
                        Pid = process.Id,
                        Name = process.ProcessName,
                        Title = SafeTitle(process),
                        WorkingSetBytes = process.WorkingSet64,
                        CpuPercent = cpu,
                    });
                }
                catch (Exception)
                {
                    // process exited mid-enumeration — skip
                }
            }
        }

        // Drop stale CPU samples for processes that have exited.
        foreach (var pid in _last.Keys.Where(k => !seen.Contains(k)).ToList())
            _last.Remove(pid);

        return entries
            .OrderByDescending(e => e.WorkingSetBytes)
            .ToList();
    }

    private static string SafeTitle(Process process)
    {
        try { return process.MainWindowTitle; } catch (Exception) { return ""; }
    }

    // Ending any of these (the app runs elevated) bugchecks Windows with CRITICAL_PROCESS_DIED or kills the
    // session, so they are refused outright rather than left to a generic "unsaved work" confirmation.
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Secure System", "MemCompression", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "LsaIso", "dwm",
    };

    public static bool IsCritical(string processName) => CriticalProcesses.Contains(processName);

    public bool EndProcess(int pid)
    {
        try
        {
            if (pid <= 4 || pid == Environment.ProcessId) return false;
            using var process = Process.GetProcessById(pid);
            if (IsCritical(process.ProcessName)) return false;
            // Only the process itself, like Task Manager's "End task". Killing the whole tree would take
            // down every app launched from it, e.g. all of the user's windows when restarting explorer.exe.
            process.Kill(entireProcessTree: false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
