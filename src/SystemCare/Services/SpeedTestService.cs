using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Net.NetworkInformation;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ISpeedTestService
{
    /// <summary>Runs latency, download and upload tests against Cloudflare's speed endpoint.</summary>
    Task<SpeedTestResult> RunAsync(Action<string> onStatus, CancellationToken ct);
}

public class SpeedTestService : ISpeedTestService
{
    // 2.19.x rewrite: a single TCP stream over a fixed byte count can never saturate a fast
    // line — at ~900 Mbps a 25 MB download finishes inside TCP slow-start and reports a fifth
    // of the real speed. Real speed tests (Ookla, fast.com) run several parallel connections
    // and measure steady-state throughput over a time window after a warm-up. Same here.
    private const string DownUrl = "https://speed.cloudflare.com/__down?bytes=100000000"; // per-stream chunk, re-requested until the window closes
    private const string UpUrl = "https://speed.cloudflare.com/__up";
    private const int DownloadStreams = 6;
    private const int UploadStreams = 4;
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(1.5); // TCP slow-start / TLS excluded
    private static readonly TimeSpan MeasureWindow = TimeSpan.FromSeconds(6);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly ILogService _log;

    public SpeedTestService(ILogService log) => _log = log;

    public async Task<SpeedTestResult> RunAsync(Action<string> onStatus, CancellationToken ct)
    {
        // Measure each leg independently so one failing endpoint (e.g. the upload host) doesn't discard a
        // perfectly good download/ping result. Cancellation still aborts the whole run.
        long latency = 0;
        double down = 0, up = 0;
        int succeeded = 0;
        var unavailable = new List<string>();

        onStatus("Measuring latency…");
        try { latency = await MeasureLatencyAsync(ct); if (latency > 0) succeeded++; else unavailable.Add("ping"); }
        catch (OperationCanceledException) { return Cancelled(); }
        catch (Exception ex) { unavailable.Add("ping"); _log.Warn("SpeedTest", $"Latency failed: {ex.Message}"); }

        onStatus("Testing download speed…");
        try { down = await MeasureDownloadAsync(ct); succeeded++; }
        catch (OperationCanceledException) { return Cancelled(); }
        catch (Exception ex) { unavailable.Add("download"); _log.Warn("SpeedTest", $"Download failed: {ex.Message}"); }

        onStatus("Testing upload speed…");
        try { up = await MeasureUploadAsync(ct); succeeded++; }
        catch (OperationCanceledException) { return Cancelled(); }
        catch (Exception ex) { unavailable.Add("upload"); _log.Warn("SpeedTest", $"Upload failed: {ex.Message}"); }

        if (succeeded == 0)
            return new SpeedTestResult(0, 0, 0, false, "Speed test failed — check your internet connection.");

        string message = "Speed test complete."
            + (unavailable.Count > 0 ? $" ({string.Join(", ", unavailable)} unavailable)" : "");
        return new SpeedTestResult(down, up, latency, true, message);
    }

    private static SpeedTestResult Cancelled() => new(0, 0, 0, false, "Cancelled.");

    private static async Task<long> MeasureLatencyAsync(CancellationToken ct)
    {
        var times = new List<long>();
        using var ping = new Ping();
        for (int i = 0; i < 4; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync("1.1.1.1", 3000);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch (Exception) { }
        }
        return times.Count > 0 ? (long)times.Average() : 0;
    }

    private static Task<double> MeasureDownloadAsync(CancellationToken ct) =>
        MeasureSteadyStateAsync(DownloadStreams, DownloadWorkerAsync, ct);

    private static Task<double> MeasureUploadAsync(CancellationToken ct) =>
        MeasureSteadyStateAsync(UploadStreams, UploadWorkerAsync, ct);

    /// <summary>
    /// Runs N parallel transfer workers and measures aggregate throughput between two snapshots:
    /// one after the warm-up (skipping TCP slow-start and TLS handshakes) and one when the window
    /// closes. Workers loop until cancelled so the pipe stays full for the whole window.
    /// </summary>
    private static async Task<double> MeasureSteadyStateAsync(
        int streams, Func<StrongBox<long>, CancellationToken, Task> worker, CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var total = new StrongBox<long>(0);

        var workers = new Task[streams];
        for (int i = 0; i < streams; i++)
            workers[i] = worker(total, window.Token);

        var sw = Stopwatch.StartNew();
        await Task.Delay(WarmUp, ct);
        long startBytes = Interlocked.Read(ref total.Value);
        double startSeconds = sw.Elapsed.TotalSeconds;

        await Task.Delay(MeasureWindow, ct);
        long endBytes = Interlocked.Read(ref total.Value);
        double endSeconds = sw.Elapsed.TotalSeconds;

        window.Cancel();
        try { await Task.WhenAll(workers); } catch (Exception) { /* workers end by cancellation/short reads */ }

        long measured = endBytes - startBytes;
        if (measured <= 0)
            throw new InvalidOperationException("no data flowed — endpoint unreachable?");
        return ToMbps(measured, endSeconds - startSeconds);
    }

    private static async Task DownloadWorkerAsync(StrongBox<long> total, CancellationToken ct)
    {
        var buffer = new byte[131072];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var resp = await Http.GetAsync(DownUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                    Interlocked.Add(ref total.Value, read);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task UploadWorkerAsync(StrongBox<long> total, CancellationToken ct)
    {
        try
        {
            // One long chunked POST per worker: bytes are counted as they are written, so the
            // measurement works at any line speed (a fixed-size POST would complete zero times
            // inside the window on slow links, and add per-request overhead on fast ones).
            using var content = new CountingStreamContent(total, ct);
            using var resp = await Http.PostAsync(UpUrl, content, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* upload endpoint hiccup — other workers keep measuring */ }
    }

    /// <summary>Streams random 64 KB chunks until the window token fires, counting written bytes.</summary>
    private sealed class CountingStreamContent(StrongBox<long> total, CancellationToken windowCt) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var chunk = new byte[65536];
            Random.Shared.NextBytes(chunk);
            try
            {
                while (!windowCt.IsCancellationRequested)
                {
                    await stream.WriteAsync(chunk, windowCt);
                    Interlocked.Add(ref total.Value, chunk.Length);
                }
            }
            catch (OperationCanceledException) { }
            // Returning normally ends the chunked body cleanly, so the POST completes.
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false; // chunked transfer
        }
    }

    private static double ToMbps(long bytes, double seconds)
        => seconds <= 0 ? 0 : Math.Round(bytes * 8.0 / seconds / 1_000_000.0, 1);
}
