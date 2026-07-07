using System.Diagnostics;
using System.Net.Http;
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
    private const string DownUrl = "https://speed.cloudflare.com/__down?bytes=25000000"; // 25 MB
    private const string UpUrl = "https://speed.cloudflare.com/__up";
    private const int UploadBytes = 10_000_000; // 10 MB

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

    private static async Task<double> MeasureDownloadAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var resp = await Http.GetAsync(DownUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0) total += read;
        sw.Stop();

        return ToMbps(total, sw.Elapsed.TotalSeconds);
    }

    private static async Task<double> MeasureUploadAsync(CancellationToken ct)
    {
        var payload = new byte[UploadBytes];
        Random.Shared.NextBytes(payload);

        var sw = Stopwatch.StartNew();
        using var content = new ByteArrayContent(payload);
        using var resp = await Http.PostAsync(UpUrl, content, ct);
        resp.EnsureSuccessStatusCode();
        sw.Stop();

        return ToMbps(UploadBytes, sw.Elapsed.TotalSeconds);
    }

    private static double ToMbps(long bytes, double seconds)
        => seconds <= 0 ? 0 : Math.Round(bytes * 8.0 / seconds / 1_000_000.0, 1);
}
