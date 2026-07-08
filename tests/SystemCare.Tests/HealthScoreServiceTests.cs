using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="HealthScoreService.Compute"/> is the revamped weighted penalty model (v2):
///   security: min(30, 10 * issues)
///   disk:     (0 &lt; free% &lt; 20) ? min(25, (20 - free%) * 1.25) : 0   (0% free = unknown ⇒ no penalty)
///   ram:      load &lt;= 70 ? 0 : min(20, (load - 70) * 0.8)
///   startup:  min(15, 2.5 * max(0, items - 6))
///   junk:     min(10, 10 * bytes / 5GiB)   (== 2 points per GiB)
///   score = clamp(100 - sum, 0, 100), truncated; band by 90/70/40 thresholds.
/// Security posture and low system-drive space dominate; junk and startup are deliberately gentle.
/// </summary>
public class HealthScoreServiceTests
{
    private readonly HealthScoreService _sut = new();

    private const long Gib = 1024L * 1024 * 1024;

    private static HealthInputs Inputs(long junk = 0, int startup = 0, double ram = 0, int security = 0, double diskFree = 0) =>
        new()
        {
            JunkBytes = junk,
            EnabledStartupItems = startup,
            RamLoadPercent = ram,
            SecurityIssues = security,
            SystemDriveFreePercent = diskFree,
        };

    [Fact]
    public void Compute_PristineSystem_ReturnsPerfectScore()
    {
        var report = _sut.Compute(Inputs());

        Assert.Equal(100, report.Score);
        Assert.Equal(HealthBand.Excellent, report.Band);
        Assert.Equal(0, report.JunkPenalty);
        Assert.Equal(0, report.StartupPenalty);
        Assert.Equal(0, report.RamPenalty);
        Assert.Equal(0, report.SecurityPenalty);
        Assert.Equal(0, report.DiskPenalty);
    }

    // (junk, startup, ram, security, diskFree, expectedScore, expectedBand)
    [Theory]
    // Junk: 2 points per GiB, capped at 10 — even lots of junk barely moves the score.
    [InlineData(Gib / 2, 0, 0, 0, 0, 99, HealthBand.Excellent)]
    [InlineData(Gib, 0, 0, 0, 0, 98, HealthBand.Excellent)]
    [InlineData(5 * Gib, 0, 0, 0, 0, 90, HealthBand.Excellent)]
    [InlineData(10 * Gib, 0, 0, 0, 0, 90, HealthBand.Excellent)]     // junk capped at 10
    // Startup: free allowance of 6, then 2.5 each, capped at 15.
    [InlineData(0, 6, 0, 0, 0, 100, HealthBand.Excellent)]
    [InlineData(0, 8, 0, 0, 0, 95, HealthBand.Excellent)]
    [InlineData(0, 12, 0, 0, 0, 85, HealthBand.Good)]
    [InlineData(0, 100, 0, 0, 0, 85, HealthBand.Good)]               // startup capped at 15
    // RAM: nothing at/below 70% load; 0.8 per point, capped at 20.
    [InlineData(0, 0, 70, 0, 0, 100, HealthBand.Excellent)]
    [InlineData(0, 0, 80, 0, 0, 92, HealthBand.Excellent)]
    [InlineData(0, 0, 95, 0, 0, 80, HealthBand.Good)]
    [InlineData(0, 0, 1000, 0, 0, 80, HealthBand.Good)]              // ram capped at 20
    // Security: 10 per issue, capped at 30 — the heaviest factor.
    [InlineData(0, 0, 0, 1, 0, 90, HealthBand.Excellent)]
    [InlineData(0, 0, 0, 3, 0, 70, HealthBand.Good)]
    [InlineData(0, 0, 0, 10, 0, 70, HealthBand.Good)]                // security capped at 30
    // Disk: no penalty at/above 20% free (or 0 = unknown); ramps to 25 at 0% free.
    [InlineData(0, 0, 0, 0, 20, 100, HealthBand.Excellent)]
    [InlineData(0, 0, 0, 0, 25, 100, HealthBand.Excellent)]
    [InlineData(0, 0, 0, 0, 12, 90, HealthBand.Excellent)]           // (20-12)*1.25 = 10
    [InlineData(0, 0, 0, 0, 8, 85, HealthBand.Good)]                 // (20-8)*1.25 = 15
    [InlineData(0, 0, 0, 0, 4, 80, HealthBand.Good)]                 // (20-4)*1.25 = 20
    // Combined band transitions.
    [InlineData(0, 0, 0, 3, 4, 50, HealthBand.NeedsAttention)]       // security 30 + disk 20
    [InlineData(5 * Gib, 12, 95, 4, 4, 5, HealthBand.Poor)]          // 10 + 15 + 20 + 30 + 20 = 95
    public void Compute_ScoresAndBands(long junk, int startup, double ram, int security, double diskFree, int expectedScore, HealthBand expectedBand)
    {
        var report = _sut.Compute(Inputs(junk, startup, ram, security, diskFree));

        Assert.Equal(expectedScore, report.Score);
        Assert.Equal(expectedBand, report.Band);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Gib, 2)]
    [InlineData(5 * Gib, 10)]
    [InlineData(10 * Gib, 10)]      // capped
    public void Compute_JunkPenalty_IsCappedAt10(long junk, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(junk: junk)).JunkPenalty, 3);
    }

    [Theory]
    [InlineData(70, 0)]             // at/below 70% load: no penalty
    [InlineData(69, 0)]
    [InlineData(80, 8)]
    [InlineData(95, 20)]            // (95-70)*0.8 = 20
    [InlineData(1000, 20)]          // capped
    public void Compute_RamPenalty_OnlyAbove70AndCappedAt20(double ram, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(ram: ram)).RamPenalty, 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]              // 6 items is the free allowance
    [InlineData(8, 5)]
    [InlineData(100, 15)]           // capped
    public void Compute_StartupPenalty_FreeBelow7AndCappedAt15(int items, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(startup: items)).StartupPenalty, 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    [InlineData(10, 30)]            // capped
    public void Compute_SecurityPenalty_IsCappedAt30(int issues, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(security: issues)).SecurityPenalty, 3);
    }

    [Theory]
    [InlineData(0, 0)]              // unknown ⇒ no penalty
    [InlineData(25, 0)]             // plenty of free space
    [InlineData(20, 0)]             // boundary
    [InlineData(12, 10)]
    [InlineData(8, 15)]
    [InlineData(4, 20)]
    public void Compute_DiskPenalty_OnlyBelow20PercentFreeAndCappedAt25(double freePercent, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(diskFree: freePercent)).DiskPenalty, 3);
    }
}
