namespace SystemCare.Services;

public interface IMonthlyReportService
{
    /// <summary>
    /// Monthly Care Report (2.16): when enabled and ≥30 days have passed since the last export,
    /// writes the Care Report HTML to Documents\SystemCare and announces it with a tray balloon.
    /// The first check after enabling only sets the baseline (no instant report). Best-effort;
    /// never throws. Call once per interactive app start.
    /// </summary>
    Task CheckAsync();
}

public sealed class MonthlyReportService(
    ICareReportExporter exporter,
    ISettingsService settings,
    ITrayIconService tray,
    IHistoryService history,
    ILogService log) : IMonthlyReportService
{
    public async Task CheckAsync()
    {
        try
        {
            if (!settings.Current.MonthlyReportEnabled) return;

            if (settings.Current.LastMonthlyReportUtc is not DateTime last)
            {
                // Just enabled: start the 30-day clock instead of exporting a mostly-empty report.
                settings.Current.LastMonthlyReportUtc = DateTime.UtcNow;
                settings.Save();
                return;
            }

            if (DateTime.UtcNow - last < TimeSpan.FromDays(30)) return;

            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemCare");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"SystemCare-report-{DateTime.Now:yyyy-MM}.html");

            await exporter.ExportAsync(path);

            settings.Current.LastMonthlyReportUtc = DateTime.UtcNow;
            settings.Save();

            tray.ShowBalloon("Monthly care report ready",
                $"What SystemCare did for this PC over the last month — saved to Documents\\SystemCare.");
            history.Record("Care report", $"Monthly report exported: {Path.GetFileName(path)}", icon: "DataTrending24");
            log.Info("MonthlyReport", $"Exported {path}");
        }
        catch (Exception ex)
        {
            log.Error("MonthlyReport", "Monthly report export failed", ex);
        }
    }
}
