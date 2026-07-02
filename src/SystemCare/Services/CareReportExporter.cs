using System.IO;
using System.Net;
using System.Text;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ICareReportExporter
{
    /// <summary>Gathers system specs + care history and writes a self-contained HTML report.
    /// Returns the written path.</summary>
    Task<string> ExportAsync(string filePath);
}

/// <summary>
/// Builds the one-file HTML care report: system specs, current health, activity totals and
/// per-category breakdown, health-score and benchmark trends. No external assets or scripts —
/// the file is printable and safe to share. HTML assembly is a pure function
/// (<see cref="BuildHtml"/>) so escaping and content rules are unit-testable.
/// </summary>
public class CareReportExporter(
    IHardwareInfoService hardware,
    ISettingsService settings,
    IHealthTrendService healthTrend,
    IHistoryService history,
    IBenchmarkHistoryService benchHistory,
    IUpdateService updates) : ICareReportExporter
{
    public async Task<string> ExportAsync(string filePath)
    {
        var report = await hardware.GetReportAsync(); // slow WMI sweep on first call; cached after

        var data = new CareReportData
        {
            AppVersion = updates.CurrentVersion,
            HealthScore = settings.Current.LastHealthScore,
            Specs = report.Specs,
            History = history.GetAll(),
            HealthTrend = healthTrend.GetAll(),
            BenchmarkRuns = benchHistory.GetAll(),
        };

        await File.WriteAllTextAsync(filePath, BuildHtml(data), Encoding.UTF8);
        return filePath;
    }

    internal static string BuildHtml(CareReportData data)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var totals = CareReportAggregator.Totals(data.History);
        var categories = CareReportAggregator.CategoryBreakdown(data.History);

        var sb = new StringBuilder(32 * 1024);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>SystemCare Report</title><style>");
        sb.Append(
            "body{background:#0A0E14;color:#D8E2EC;font-family:'Segoe UI',system-ui,sans-serif;margin:0;padding:32px;}" +
            "h1{color:#00E5FF;font-size:26px;margin:0 0 2px;}h2{color:#00E5FF;font-size:15px;letter-spacing:2px;" +
            "text-transform:uppercase;margin:32px 0 10px;border-bottom:1px solid #1B2431;padding-bottom:6px;}" +
            ".sub{opacity:.6;font-size:12px;margin-bottom:24px;}" +
            ".tiles{display:flex;gap:12px;flex-wrap:wrap;}" +
            ".tile{background:#111826;border:1px solid #1B2431;border-radius:10px;padding:14px 20px;min-width:150px;}" +
            ".tile .v{font-size:24px;color:#00E5FF;font-weight:600;}.tile .l{font-size:11px;opacity:.6;text-transform:uppercase;letter-spacing:1px;}" +
            "table{border-collapse:collapse;width:100%;font-size:13px;}" +
            "th{text-align:left;opacity:.55;font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:1px;padding:6px 10px;}" +
            "td{padding:6px 10px;border-top:1px solid #151D2B;}" +
            "tr:hover td{background:#101725;}" +
            ".num{text-align:right;white-space:nowrap;}" +
            ".foot{margin-top:36px;font-size:11px;opacity:.45;}" +
            "@media print{body{background:#fff;color:#111;}h1,h2,.tile .v{color:#0077A0;}.tile{border-color:#ccc;background:#f6f8fa;}td{border-color:#e2e2e2;}}");
        sb.Append("</style></head><body>");

        sb.Append("<h1>SystemCare Report</h1>");
        sb.Append($"<div class=\"sub\">Generated {E(data.GeneratedLocal.ToString("f"))} · SystemCare {E(data.AppVersion)}</div>");

        // Headline tiles
        sb.Append("<div class=\"tiles\">");
        AppendTile(sb, data.HealthScore is int score ? $"{score}/100" : "—", "Health score");
        AppendTile(sb, ByteFormatter.Format(totals.TotalBytes), "Space freed");
        AppendTile(sb, totals.TotalActions.ToString("N0"), "Actions recorded");
        var lastBench = data.BenchmarkRuns.Count > 0 ? data.BenchmarkRuns[^1] : null;
        AppendTile(sb, lastBench is null ? "—" : $"{lastBench.Points:N0} pts", "Latest benchmark");
        sb.Append("</div>");

        // System specs
        sb.Append("<h2>System</h2><table><tr><th>Component</th><th>Details</th></tr>");
        foreach (var spec in data.Specs)
            sb.Append($"<tr><td>{E(spec.Name)}</td><td>{E(spec.Detail)}{(spec.Health is null ? "" : $" · {E(spec.Health)}")}</td></tr>");
        if (data.Specs.Count == 0) sb.Append("<tr><td colspan=\"2\">No hardware information collected.</td></tr>");
        sb.Append("</table>");

        // Maintenance activity
        sb.Append("<h2>Maintenance activity</h2>");
        if (totals.OldestUtc is DateTime oldest)
            sb.Append($"<div class=\"sub\">Based on the last {totals.TotalActions:N0} recorded action(s), since {E(oldest.ToLocalTime().ToString("d"))} (history keeps the most recent 500).</div>");
        sb.Append("<table><tr><th>Category</th><th class=\"num\">Actions</th><th class=\"num\">Space freed</th></tr>");
        foreach (var row in categories)
            sb.Append($"<tr><td>{E(row.Category)}</td><td class=\"num\">{row.Count:N0}</td><td class=\"num\">{ByteFormatter.Format(row.Bytes)}</td></tr>");
        if (categories.Count == 0) sb.Append("<tr><td colspan=\"3\">No maintenance recorded yet.</td></tr>");
        sb.Append("</table>");

        // Health trend
        sb.Append("<h2>Health score trend</h2>");
        if (data.HealthTrend.Count == 0)
        {
            sb.Append("<div class=\"sub\">No health scans recorded yet — run a scan from the Dashboard or Auto Care.</div>");
        }
        else
        {
            sb.Append("<table><tr><th>Date</th><th class=\"num\">Score</th></tr>");
            foreach (var snap in data.HealthTrend.TakeLast(30))
                sb.Append($"<tr><td>{E(snap.TimestampUtc.ToLocalTime().ToString("d"))}</td><td class=\"num\">{snap.Score}</td></tr>");
            sb.Append("</table>");
        }

        // Benchmark trend
        sb.Append("<h2>Benchmark history</h2>");
        if (data.BenchmarkRuns.Count == 0)
        {
            sb.Append("<div class=\"sub\">No benchmark runs recorded yet.</div>");
        }
        else
        {
            sb.Append("<table><tr><th>Date</th><th class=\"num\">Points</th><th class=\"num\">CPU</th><th class=\"num\">RAM</th><th class=\"num\">Disk</th></tr>");
            foreach (var run in data.BenchmarkRuns)
                sb.Append($"<tr><td>{E(run.TimestampUtc.ToLocalTime().ToString("g"))}</td><td class=\"num\">{run.Points:N0}</td>" +
                          $"<td class=\"num\">{run.CpuMOps:N0} MOps/s</td><td class=\"num\">{run.RamGBps:0.0} GB/s</td><td class=\"num\">{run.DiskMBps:N0} MB/s</td></tr>");
            sb.Append("</table>");
        }

        // Recent actions
        sb.Append("<h2>Recent actions</h2>");
        if (data.History.Count == 0)
        {
            sb.Append("<div class=\"sub\">No activity recorded yet.</div>");
        }
        else
        {
            sb.Append("<table><tr><th>When</th><th>Category</th><th>What happened</th></tr>");
            foreach (var entry in data.History.Take(20))
                sb.Append($"<tr><td>{E(entry.TimestampUtc.ToLocalTime().ToString("g"))}</td><td>{E(entry.Category)}</td><td>{E(entry.Summary)}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<div class=\"foot\">Generated locally by SystemCare — nothing in this report was sent anywhere.</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendTile(StringBuilder sb, string value, string label) =>
        sb.Append($"<div class=\"tile\"><div class=\"v\">{WebUtility.HtmlEncode(value)}</div><div class=\"l\">{WebUtility.HtmlEncode(label)}</div></div>");
}
