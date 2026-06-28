using System.Diagnostics;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IBenchmarkService
{
    /// <summary>Run the CPU/RAM/disk benchmark off the UI thread, reporting per-phase progress.</summary>
    Task<BenchmarkResult> RunAsync(IProgress<BenchmarkProgress>? progress, CancellationToken ct);
}

/// <summary>
/// A quick, fully-local performance benchmark. Each test is wall-clock-budgeted so it stays snappy on any
/// machine, runs entirely on a background thread, and is cancellable. Results are relative (calibrated by
/// <see cref="BenchmarkScoring"/>), not industry-standard figures.
/// </summary>
public sealed class BenchmarkService : IBenchmarkService
{
    private const int CpuBudgetMs = 1500;                  // multi-thread compute window
    private const int RamBufferBytes = 128 * 1024 * 1024;  // 128 MB working set per buffer
    private const int RamBudgetMs = 1200;
    private const long DiskFileBytes = 256L * 1024 * 1024; // 256 MB temp file
    private const int DiskBlockBytes = 1 * 1024 * 1024;    // 1 MB IO blocks

    public Task<BenchmarkResult> RunAsync(IProgress<BenchmarkProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        progress?.Report(new BenchmarkProgress { Phase = "Testing CPU…", Percent = 5 });
        double cpuMOps = RunCpu(ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new BenchmarkProgress { Phase = "Testing memory…", Percent = 45 });
        double ramGBps = RunRam(ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new BenchmarkProgress { Phase = "Testing disk…", Percent = 70 });
        double diskMBps = RunDisk(ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new BenchmarkProgress { Phase = "Scoring…", Percent = 98 });
        double cpuScore = BenchmarkScoring.CpuScore(cpuMOps);
        double ramScore = BenchmarkScoring.RamScore(ramGBps);
        double diskScore = BenchmarkScoring.DiskScore(diskMBps);
        double overall = BenchmarkScoring.Overall(cpuScore, ramScore, diskScore);

        progress?.Report(new BenchmarkProgress { Phase = "Done", Percent = 100 });
        return new BenchmarkResult
        {
            CpuMOps = cpuMOps,
            RamGBps = ramGBps,
            DiskMBps = diskMBps,
            CpuScore = cpuScore,
            RamScore = ramScore,
            DiskScore = diskScore,
            OverallIndex = overall,
            Points = BenchmarkScoring.Points(overall),
        };
    }, ct);

    // ---- CPU: multi-thread xorshift throughput (MOps/s) ----
    private static double RunCpu(CancellationToken ct)
    {
        int threads = Math.Max(1, Environment.ProcessorCount);
        var counts = new long[threads];
        var sw = Stopwatch.StartNew();
        long budgetTicks = Stopwatch.Frequency * CpuBudgetMs / 1000;

        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct }, t =>
        {
            ulong x = 0x9E3779B97F4A7C15UL + (ulong)t * 0x100000001B3UL;
            long local = 0;
            const int chunk = 1_000_000;
            while (sw.ElapsedTicks < budgetTicks)
            {
                for (int i = 0; i < chunk; i++)
                {
                    x ^= x << 13;
                    x ^= x >> 7;
                    x ^= x << 17;
                    x += 0x9E3779B97F4A7C15UL;
                }
                local += chunk;
                if (x == 0) local++; // sink: stops the JIT eliminating the loop as dead code
            }
            counts[t] = local;
        });
        sw.Stop();

        long total = 0;
        foreach (var c in counts) total += c;
        double sec = sw.Elapsed.TotalSeconds;
        return sec > 0 ? total / sec / 1e6 : 0;
    }

    // ---- RAM: sequential copy bandwidth (GB/s) ----
    private static double RunRam(CancellationToken ct)
    {
        int n = RamBufferBytes / sizeof(long);
        var a = new long[n];
        var b = new long[n];
        for (int i = 0; i < n; i++) a[i] = i; // fault the pages in before timing

        var sw = Stopwatch.StartNew();
        long budgetTicks = Stopwatch.Frequency * RamBudgetMs / 1000;
        long bytesMoved = 0;
        while (sw.ElapsedTicks < budgetTicks)
        {
            ct.ThrowIfCancellationRequested();
            Array.Copy(a, b, n);                       // one read pass + one write pass
            bytesMoved += (long)n * sizeof(long) * 2;
            (a, b) = (b, a);                           // swap so neither side is optimized away
        }
        sw.Stop();
        double sec = sw.Elapsed.TotalSeconds;
        return sec > 0 ? bytesMoved / sec / 1e9 : 0;
    }

    // ---- Disk: sequential write-through throughput (MB/s) ----
    private static double RunDisk(CancellationToken ct)
    {
        string path = Path.Combine(Path.GetTempPath(), $"SystemCare_bench_{Guid.NewGuid():N}.tmp");
        var block = new byte[DiskBlockBytes];
        new Random(20260625).NextBytes(block);
        try
        {
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                       DiskBlockBytes, FileOptions.WriteThrough))
            {
                long written = 0;
                while (written < DiskFileBytes)
                {
                    ct.ThrowIfCancellationRequested();
                    fs.Write(block, 0, block.Length);
                    written += block.Length;
                }
                fs.Flush(true); // push past OS buffers so the number reflects the drive, not cache
            }
            sw.Stop();
            double sec = sw.Elapsed.TotalSeconds;
            return sec > 0 ? DiskFileBytes / sec / 1e6 : 0;
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
