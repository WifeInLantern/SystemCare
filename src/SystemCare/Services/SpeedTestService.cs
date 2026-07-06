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
        try
        {
            onStatus("Measuring latency…");
            long latency = await MeasureLatencyAsync(ct);

            onStatus("Testing download speed…");
            double down = await MeasureDownloadAsync(ct);

            onStatus("Testing upload speed…");
            double up = await MeasureUploadAsync(ct);

            onStatus("Done.");
            return new SpeedTestResult(down, up, latency, true, "Speed test complete.");
        }
        catch (OperationCanceledException)
        {
            return new SpeedTestResult(0, 0, 0, false, "Cancelled.");
        }
        catch (Exception ex)
        {
            _log.Warn("SpeedTest", $"Failed: {ex.Message}");
            return new SpeedTestResult(0, 0, 0, false, "Speed test failed — check your internet connection.");
        }
    }

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
