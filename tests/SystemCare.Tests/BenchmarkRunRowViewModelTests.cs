using SystemCare.Models;
using SystemCare.ViewModels;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="BenchmarkRunRowViewModel"/> formats a stored run's raw metrics for the
/// "Run details" expander: rounded raw throughputs with units, headline points, and the
/// three sub-scores on one line.
/// </summary>
public class BenchmarkRunRowViewModelTests
{
    private static BenchmarkRun SampleRun() => new()
    {
        TimestampUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
        CpuMOps = 1234.5,
        RamGBps = 21.37,
        DiskMBps = 987.6,
        CpuScore = 61.2,
        RamScore = 55.9,
        DiskScore = 48.4,
        OverallIndex = 55.0,
        Points = 5500,
    };

    [Fact]
    public void RawMetrics_UseExpectedUnitsAndPrecision()
    {
        var row = new BenchmarkRunRowViewModel(SampleRun());

        // Culture-aware group separators: compare against the same format the VM uses.
        Assert.Equal($"{1234.5:N0} MOps/s", row.CpuRaw);   // rounded to whole MOps
        Assert.Equal($"{21.37:0.0} GB/s", row.RamRaw);      // one decimal
        Assert.Equal($"{987.6:N0} MB/s", row.DiskRaw);      // rounded to whole MB/s
    }

    [Fact]
    public void PointsText_FormatsWithGroupSeparatorAndUnit()
    {
        var row = new BenchmarkRunRowViewModel(SampleRun());
        Assert.Equal($"{5500:N0} pts", row.PointsText);
    }

    [Fact]
    public void SubScores_RoundsEachScoreToWholeNumbers()
    {
        var row = new BenchmarkRunRowViewModel(SampleRun());
        Assert.Equal("CPU 61 · RAM 56 · Disk 48", row.SubScores);
    }

    [Fact]
    public void When_ConvertsUtcTimestampToLocalTime()
    {
        var run = SampleRun();
        var row = new BenchmarkRunRowViewModel(run);
        Assert.Equal(run.TimestampUtc.ToLocalTime().ToString("g"), row.When);
    }
}
