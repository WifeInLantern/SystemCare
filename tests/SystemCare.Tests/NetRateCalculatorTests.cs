using SystemCare.Helpers;
using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="NetRateCalculator"/> turns two cumulative per-PID byte snapshots into rates:
/// deltas divided by the interval, negative deltas (counter reset / PID reuse) clamped to zero,
/// first-tick PIDs treated as accumulated over this interval, plus share/bar percentages and
/// the traffic-level thresholds used for row colouring.
/// </summary>
public class NetRateCalculatorTests
{
    private static Dictionary<int, ProcessNetSample> Prev(params ProcessNetSample[] samples) =>
        samples.ToDictionary(s => s.Pid);

    [Fact]
    public void Compute_DividesDeltasByInterval()
    {
        var prev = Prev(new ProcessNetSample(1, SentBytes: 1000, RecvBytes: 2000));
        var snap = new[] { new ProcessNetSample(1, SentBytes: 3000, RecvBytes: 6000) };

        var result = NetRateCalculator.Compute(prev, snap, intervalSeconds: 2.0);

        var r = Assert.Single(result.Rates);
        Assert.Equal(2000, r.Down);   // (6000-2000)/2
        Assert.Equal(1000, r.Up);     // (3000-1000)/2
        Assert.Equal(3000, r.Combined);
        Assert.Equal(6000, r.TotalDown);
        Assert.Equal(3000, r.TotalUp);
    }

    [Fact]
    public void Compute_FirstTick_UsesSnapshotTotalsAsDelta()
    {
        var snap = new[] { new ProcessNetSample(7, SentBytes: 500, RecvBytes: 1500) };

        var result = NetRateCalculator.Compute(new Dictionary<int, ProcessNetSample>(), snap, 1.0);

        var r = Assert.Single(result.Rates);
        Assert.Equal(1500, r.Down);
        Assert.Equal(500, r.Up);
    }

    [Fact]
    public void Compute_NegativeDelta_ClampsToZero()
    {
        // Counter reset (session restart) or PID reuse: previous totals exceed current ones.
        var prev = Prev(new ProcessNetSample(1, SentBytes: 9000, RecvBytes: 9000));
        var snap = new[] { new ProcessNetSample(1, SentBytes: 100, RecvBytes: 100) };

        var result = NetRateCalculator.Compute(prev, snap, 1.0);

        var r = Assert.Single(result.Rates);
        Assert.Equal(0, r.Down);
        Assert.Equal(0, r.Up);
    }

    [Fact]
    public void Compute_TinyInterval_ClampsToQuarterSecond()
    {
        var snap = new[] { new ProcessNetSample(1, SentBytes: 0, RecvBytes: 1000) };

        // 1000 bytes over a clamped 0.25s interval = 4000 B/s, not 100000 B/s.
        var result = NetRateCalculator.Compute(new Dictionary<int, ProcessNetSample>(), snap, 0.01);

        Assert.Equal(4000, Assert.Single(result.Rates).Down);
    }

    [Fact]
    public void Compute_AggregatesTotalAndMaxAcrossProcesses()
    {
        var snap = new[]
        {
            new ProcessNetSample(1, SentBytes: 100, RecvBytes: 300),  // combined 400
            new ProcessNetSample(2, SentBytes: 50, RecvBytes: 50),    // combined 100
        };

        var result = NetRateCalculator.Compute(new Dictionary<int, ProcessNetSample>(), snap, 1.0);

        Assert.Equal(500, result.TotalCombined);
        Assert.Equal(400, result.MaxCombined);
    }

    [Theory]
    [InlineData(0, NetUsageLevel.Low)]
    [InlineData(256 * 1024 - 1, NetUsageLevel.Low)]
    [InlineData(256 * 1024, NetUsageLevel.Medium)]           // 256 KB/s boundary
    [InlineData(2 * 1024 * 1024 - 1, NetUsageLevel.Medium)]
    [InlineData(2 * 1024 * 1024, NetUsageLevel.High)]        // 2 MB/s boundary
    public void LevelFor_UsesDocumentedThresholds(double combined, NetUsageLevel expected)
    {
        Assert.Equal(expected, NetRateCalculator.LevelFor(combined));
    }

    [Fact]
    public void Share_ComputesPercentOfTotalAndBarRelativeToBusiest()
    {
        var (percent, bar) = NetRateCalculator.Share(combined: 100, totalCombined: 400, maxCombined: 200);
        Assert.Equal(25, percent);
        Assert.Equal(50, bar);
    }

    [Fact]
    public void Share_ZeroTotals_ReturnsZeroInsteadOfNaN()
    {
        var (percent, bar) = NetRateCalculator.Share(0, 0, 0);
        Assert.Equal(0, percent);
        Assert.Equal(0, bar);
    }
}
