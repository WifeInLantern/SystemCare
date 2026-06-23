using SystemCare.Models;

namespace SystemCare.Helpers;

/// <summary>
/// Pure text/formatting helpers for the live monitor (tray tooltip + mini-widget), kept separate from the
/// timer/interop so they are unit-testable.
/// </summary>
public static class MetricsFormatter
{
    /// <summary>CPU load as a whole-number percentage, or an em dash before the first valid sample.</summary>
    public static string Cpu(double? cpuPercent) => cpuPercent is double c ? $"{c:0}%" : "—";

    /// <summary>RAM load as a whole-number percentage.</summary>
    public static string Ram(double ramLoadPercent) => $"{ramLoadPercent:0}%";

    /// <summary>A network throughput rate, e.g. "1.2 MB/s" (negatives clamp to zero).</summary>
    public static string NetRate(double bytesPerSec) => $"{ByteFormatter.Format((long)Math.Max(0, bytesPerSec))}/s";

    /// <summary>The system-tray tooltip: a name line plus a compact CPU/RAM line.</summary>
    public static string TrayTooltip(SystemSnapshot? snapshot) =>
        snapshot is null
            ? "SystemCare"
            : $"SystemCare\nCPU {Cpu(snapshot.CpuPercent)}   RAM {Ram(snapshot.RamLoadPercent)}";

    /// <summary>Appends a sample to a rolling buffer, trimming the oldest beyond <paramref name="capacity"/>.</summary>
    public static void Push(Queue<double> buffer, double value, int capacity)
    {
        buffer.Enqueue(value);
        while (buffer.Count > capacity) buffer.Dequeue();
    }
}
