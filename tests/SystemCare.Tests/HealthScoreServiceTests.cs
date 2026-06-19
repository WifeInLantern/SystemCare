using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="HealthScoreService.Compute"/> is a pure penalty model:
///   junk:     min(40, 40 * bytes / 2GiB)
///   startup:  min(25, 3 * max(0, items - 4))
///   ram:      load &lt;= 50 ? 0 : min(35, (load - 50) * 0.7)
///   security: min(20, 8 * issues)
///   score = clamp(100 - sum, 0, 100); band by 90/70/40 thresholds.
/// Exact-boundary cases use power-of-two junk byte counts so the penalty is exact in double math.
/// </summary>
public class HealthScoreServiceTests
{
    private readonly HealthScoreService _sut = new();

    private const long Gib = 1024L * 1024 * 1024;

    private static HealthInputs Inputs(long junk = 0, int startup = 0, double ram = 0, int security = 0) =>
        new() { JunkBytes = junk, EnabledStartupItems = startup, RamLoadPercent = ram, SecurityIssues = security };

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
    }

    // (junkBytes, startupItems, ramPercent, securityIssues, expectedScore, expectedBand)
    [Theory]
    // Junk: 0.5/1/1.5/2 GiB give exact 10/20/30/40 penalties; >2 GiB stays capped at 40.
    [InlineData(Gib / 2, 0, 0, 0, 90, HealthBand.Excellent)]
    [InlineData(Gib, 0, 0, 0, 80, HealthBand.Good)]
    [InlineData(Gib + Gib / 2, 0, 0, 0, 70, HealthBand.Good)]          // exactly the Good threshold
    [InlineData(2 * Gib, 0, 0, 0, 60, HealthBand.NeedsAttention)]
    [InlineData(4 * Gib, 0, 0, 0, 60, HealthBand.NeedsAttention)]      // junk penalty capped at 40
    // Startup: nothing below 5 items; 3 per extra item; capped at 25.
    [InlineData(0, 4, 0, 0, 100, HealthBand.Excellent)]
    [InlineData(0, 5, 0, 0, 97, HealthBand.Excellent)]
    [InlineData(0, 12, 0, 0, 76, HealthBand.Good)]
    [InlineData(0, 100, 0, 0, 75, HealthBand.Good)]                   // startup penalty capped at 25
    // Security: 8 per issue; capped at 20.
    [InlineData(0, 0, 0, 1, 92, HealthBand.Excellent)]
    [InlineData(0, 0, 0, 3, 80, HealthBand.Good)]                     // min(20, 24) = 20
    [InlineData(0, 0, 0, 10, 80, HealthBand.Good)]                    // security capped at 20
    // Band transitions bracketing the 70 and 40 thresholds (exact-penalty combos).
    [InlineData(Gib + Gib / 2, 5, 0, 0, 67, HealthBand.NeedsAttention)]   // 30 + 3
    [InlineData(2 * Gib, 0, 0, 3, 40, HealthBand.NeedsAttention)]         // 40 + 20, exactly 40
    [InlineData(2 * Gib, 5, 0, 3, 37, HealthBand.Poor)]                   // 40 + 3 + 20
    // Everything maxed out clamps to zero rather than going negative.
    [InlineData(10 * Gib, 100, 1000, 10, 0, HealthBand.Poor)]
    public void Compute_ScoresAndBands(long junk, int startup, double ram, int security, int expectedScore, HealthBand expectedBand)
    {
        var report = _sut.Compute(Inputs(junk, startup, ram, security));

        Assert.Equal(expectedScore, report.Score);
        Assert.Equal(expectedBand, report.Band);
    }

    [Theory]
    [InlineData(0, 0)]       // < junk floor
    [InlineData(Gib, 20)]
    [InlineData(2 * Gib, 40)]
    [InlineData(4 * Gib, 40)] // capped
    public void Compute_JunkPenalty_IsCappedAt40(long junk, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(junk: junk)).JunkPenalty, 3);
    }

    [Theory]
    [InlineData(50, 0)]       // at/below 50% load: no penalty
    [InlineData(49, 0)]
    [InlineData(100, 35)]     // (100-50)*0.7 = 35
    [InlineData(1000, 35)]    // capped at 35
    public void Compute_RamPenalty_OnlyAbove50AndCappedAt35(double ram, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(ram: ram)).RamPenalty, 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]        // 4 items is the free allowance
    [InlineData(5, 3)]
    [InlineData(100, 25)]     // capped
    public void Compute_StartupPenalty_FreeBelow5AndCappedAt25(int items, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(startup: items)).StartupPenalty, 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 16)]
    [InlineData(3, 20)]       // min(20, 24)
    public void Compute_SecurityPenalty_IsCappedAt20(int issues, double expected)
    {
        Assert.Equal(expected, _sut.Compute(Inputs(security: issues)).SecurityPenalty, 3);
    }
}
