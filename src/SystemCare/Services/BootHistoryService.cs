using System.Text.Json;

namespace SystemCare.Services;

public sealed class BootSample
{
    public DateTime BootUtc { get; set; }
    public int DurationMs { get; set; }
}

public interface IBootHistoryService
{
    /// <summary>
    /// Boot report (2.16): records the latest boot duration and, when this boot is markedly slower
    /// than the recent median, raises one tray notification pointing at Boot Analyzer / Startup
    /// Manager. Best-effort; never throws. Call once per interactive app start.
    /// </summary>
    Task CheckAndReportAsync();
}

/// <summary>
/// Persists one sample per boot (keyed by boot time) as JSON next to settings.json — same
/// discipline as the other trend services: lock-guarded, best-effort, capped, never throws.
/// A report fires only with enough history (5+ boots) and a real regression (≥25% over median),
/// so a one-off slow boot after Windows Update doesn't nag.
/// </summary>
public sealed class BootHistoryService(
    IBootPerformanceService bootPerf,
    ITrayIconService tray,
    IHistoryService history,
    ILogService log) : IBootHistoryService
{
    private const int MaxSamples = 90;
    private const int MinSamplesForBaseline = 5;
    private const double RegressionFactor = 1.25;

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare", "boot-history.json");

    public async Task CheckAndReportAsync()
    {
        try
        {
            var report = await bootPerf.GetAsync();
            if (!report.HasBootData || report.LastBootUtc == default) return;

            var list = Load();
            var previous = list.Where(s => s.BootUtc != report.LastBootUtc).ToList();

            // Record (replace any earlier sample for the same boot).
            previous.Add(new BootSample { BootUtc = report.LastBootUtc, DurationMs = report.BootDurationMs });
            if (previous.Count > MaxSamples) previous.RemoveRange(0, previous.Count - MaxSamples);
            Save(previous);

            var baseline = previous
                .Where(s => s.BootUtc != report.LastBootUtc)
                .OrderByDescending(s => s.BootUtc)
                .Take(30)
                .Select(s => (double)s.DurationMs)
                .OrderBy(v => v)
                .ToList();
            if (baseline.Count < MinSamplesForBaseline) return;

            double median = baseline[baseline.Count / 2];
            if (median <= 0 || report.BootDurationMs < median * RegressionFactor) return;

            int slowerPercent = (int)Math.Round((report.BootDurationMs / median - 1) * 100);
            string message = $"This boot took {report.BootDurationMs / 1000.0:0.0}s — {slowerPercent}% slower than your " +
                             $"typical {median / 1000.0:0.0}s. Boot Analyzer shows what dragged; Startup Manager can disable it.";
            tray.ShowBalloon("Slower start-up than usual", message);
            history.Record("Boot report", $"Boot {report.BootDurationMs / 1000.0:0.0}s vs typical {median / 1000.0:0.0}s (+{slowerPercent}%)",
                icon: "Timer24");
            log.Info("BootReport", message);
        }
        catch (Exception ex)
        {
            log.Error("BootReport", "Boot check failed", ex);
        }
    }

    private static List<BootSample> Load()
    {
        try
        {
            return File.Exists(StorePath)
                ? JsonSerializer.Deserialize<List<BootSample>>(File.ReadAllText(StorePath)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void Save(List<BootSample> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list));
        }
        catch (Exception)
        {
            // best-effort
        }
    }
}
