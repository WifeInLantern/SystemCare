using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="CareReportExporter.BuildHtml"/> is the pure HTML assembly for the exported care
/// report: it must carry the headline numbers, HTML-escape user-influenced strings (history
/// summaries contain app names), stay self-contained (no external refs, no scripts), and render
/// gracefully with no data at all.
/// </summary>
public class CareReportExporterTests
{
    [Fact]
    public void BuildHtml_ContainsHeadlineNumbersAndSpecs()
    {
        var html = CareReportExporter.BuildHtml(new CareReportData
        {
            AppVersion = "2.4.0",
            HealthScore = 87,
            Specs = [new HardwareSpec { Category = "Processor", Name = "CPU", Detail = "Ryzen 7 5800X" }],
            History =
            [
                new HistoryEntry { Category = "Junk cleanup", Summary = "Removed stuff", BytesFreed = 1024 * 1024 * 1024 },
            ],
            BenchmarkRuns = [new BenchmarkRun { Points = 5500, CpuMOps = 1200, RamGBps = 20, DiskMBps = 900 }],
        });

        Assert.Contains("87/100", html);
        Assert.Contains("1 GB", html);
        Assert.Contains("Ryzen 7 5800X", html);
        Assert.Contains($"{5500:N0} pts", html);
        Assert.Contains("2.4.0", html);
    }

    [Fact]
    public void BuildHtml_EscapesHtmlInHistoryAndSpecStrings()
    {
        var html = CareReportExporter.BuildHtml(new CareReportData
        {
            Specs = [new HardwareSpec { Category = "Os", Name = "<script>alert(1)</script>", Detail = "a & b" }],
            History =
            [
                new HistoryEntry { Category = "Uninstalled program", Summary = "Removed <Evil & App>", BytesFreed = 1 },
            ],
        });

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Removed &lt;Evil &amp; App&gt;", html);
    }

    [Fact]
    public void BuildHtml_EmptyData_StillProducesACompleteDocument()
    {
        var html = CareReportExporter.BuildHtml(new CareReportData());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.EndsWith("</html>", html);
        Assert.Contains("No maintenance recorded yet", html);
        Assert.Contains("No benchmark runs recorded yet", html);
    }

    [Fact]
    public void BuildHtml_IsSelfContained_NoExternalReferencesOrScripts()
    {
        var html = CareReportExporter.BuildHtml(new CareReportData
        {
            History = [new HistoryEntry { Category = "Boost", Summary = "x", BytesFreed = 5 }],
        });

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<link", html);
    }
}
