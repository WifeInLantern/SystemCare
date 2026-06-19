using System.Collections.Generic;
using System.Linq;
using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="DiskHealthScoreService"/> is a pure, conservative penalty model:
///   SMART Warning caps the score at 60, Unhealthy at 20; SSD wear costs 0.5/percent;
///   temp &gt;= 70 costs 25 (&gt;= 60 costs 10); any reallocated sector caps the score at 35.
/// Alerts mirror those signals with an urgency and an optional recommended action.
/// </summary>
public class DiskHealthScoreServiceTests
{
    private readonly DiskHealthScoreService _sut = new();

    private static PhysicalDiskHealth Disk(
        DiskHealthStatus health = DiskHealthStatus.Healthy,
        int? wear = null, double? temp = null, long? realloc = null, string name = "Disk 0") =>
        new() { Name = name, Health = health, WearPercent = wear, TemperatureC = temp, ReallocatedSectors = realloc };

    // ---------- Score ----------

    public static IEnumerable<object[]> ScoreCases() => new[]
    {
        new object[] { Disk(), 100 },                                                 // healthy, no SMART data
        new object[] { Disk(wear: 0), 100 },                                          // wear 0 ignored (the >0 guard)
        new object[] { Disk(wear: 10), 95 },
        new object[] { Disk(wear: 100), 50 },
        new object[] { Disk(temp: 59.0), 100 },                                       // below the 60 threshold
        new object[] { Disk(temp: 60.0), 90 },
        new object[] { Disk(temp: 69.0), 90 },
        new object[] { Disk(temp: 70.0), 75 },
        new object[] { Disk(temp: 85.0), 75 },
        new object[] { Disk(health: DiskHealthStatus.Warning), 60 },                  // SMART warning caps at 60
        new object[] { Disk(health: DiskHealthStatus.Warning, wear: 20), 50 },        // cap to 60, then -10 for wear
        new object[] { Disk(health: DiskHealthStatus.Unhealthy), 20 },                // SMART unhealthy caps at 20
        new object[] { Disk(realloc: 1), 35 },                                        // any bad sector caps at 35
        new object[] { Disk(wear: 10, realloc: 5), 35 },                              // 95 then capped to 35
        new object[] { Disk(health: DiskHealthStatus.Unhealthy, realloc: 5), 20 },    // worst cap wins (20 < 35)
        new object[] { Disk(wear: 250), 0 },                                          // clamps at the floor
    };

    [Theory]
    [MemberData(nameof(ScoreCases))]
    public void Score_AppliesPenaltyModel(PhysicalDiskHealth disk, int expected)
    {
        Assert.Equal(expected, _sut.Score(disk));
    }

    [Fact]
    public void Score_UnknownHealth_DoesNotPenalize()
    {
        Assert.Equal(100, _sut.Score(Disk(health: DiskHealthStatus.Unknown)));
    }

    // ---------- Band ----------

    [Theory]
    [InlineData(-1, "Not scored")]
    [InlineData(-100, "Not scored")]
    [InlineData(100, "Excellent")]
    [InlineData(90, "Excellent")]
    [InlineData(89, "Good")]
    [InlineData(70, "Good")]
    [InlineData(69, "Needs attention")]
    [InlineData(40, "Needs attention")]
    [InlineData(39, "At risk")]
    [InlineData(0, "At risk")]
    public void Band_MapsScoreToLabel(int score, string expected)
    {
        Assert.Equal(expected, _sut.Band(score));
    }

    // ---------- Alerts ----------

    [Fact]
    public void Alerts_HealthyDrive_ReturnsNone()
    {
        Assert.Empty(_sut.Alerts(Disk()));
    }

    [Fact]
    public void Alerts_Unhealthy_RaisesCriticalWithRestorePointAction()
    {
        var alert = Assert.Single(_sut.Alerts(Disk(health: DiskHealthStatus.Unhealthy, name: "SSD")));

        Assert.Equal(DiskUrgency.Critical, alert.Urgency);
        Assert.Contains("may be failing", alert.Title);
        Assert.Contains("SSD", alert.Title);
        Assert.Equal("__restorepoint", alert.ActionTarget);
    }

    [Fact]
    public void Alerts_ReallocatedSectors_RaisesCriticalAndReportsTheCount()
    {
        var alert = Assert.Single(_sut.Alerts(Disk(realloc: 5)));

        Assert.Equal(DiskUrgency.Critical, alert.Urgency);
        Assert.Contains("Bad sectors", alert.Title);
        Assert.Contains("5", alert.Detail);
    }

    [Fact]
    public void Alerts_Warning_RaisesWarningWithMaintainAction()
    {
        var alert = Assert.Single(_sut.Alerts(Disk(health: DiskHealthStatus.Warning)));

        Assert.Equal(DiskUrgency.Warning, alert.Urgency);
        Assert.Equal("__maintain", alert.ActionTarget);
    }

    [Theory]
    [InlineData(90, DiskUrgency.Warning, "heavily worn")]
    [InlineData(95, DiskUrgency.Warning, "heavily worn")]
    [InlineData(80, DiskUrgency.Caution, "aging")]
    [InlineData(89, DiskUrgency.Caution, "aging")]
    public void Alerts_HighWear_RaisesGradedAlert(int wear, DiskUrgency urgency, string titleFragment)
    {
        var alert = Assert.Single(_sut.Alerts(Disk(wear: wear)));

        Assert.Equal(urgency, alert.Urgency);
        Assert.Contains(titleFragment, alert.Title);
    }

    [Fact]
    public void Alerts_WearBelowThreshold_RaisesNothing()
    {
        Assert.Empty(_sut.Alerts(Disk(wear: 79)));
    }

    [Theory]
    [InlineData(70.0, DiskUrgency.Warning, "running hot")]
    [InlineData(85.0, DiskUrgency.Warning, "running hot")]
    [InlineData(60.0, DiskUrgency.Caution, "warm")]
    [InlineData(69.0, DiskUrgency.Caution, "warm")]
    public void Alerts_HighTemperature_RaisesGradedAlert(double temp, DiskUrgency urgency, string titleFragment)
    {
        var alert = Assert.Single(_sut.Alerts(Disk(temp: temp)));

        Assert.Equal(urgency, alert.Urgency);
        Assert.Contains(titleFragment, alert.Title);
    }

    [Fact]
    public void Alerts_TemperatureBelowThreshold_RaisesNothing()
    {
        Assert.Empty(_sut.Alerts(Disk(temp: 59.0)));
    }

    [Fact]
    public void Alerts_MultipleProblems_AreAllReported()
    {
        var alerts = _sut.Alerts(Disk(health: DiskHealthStatus.Unhealthy, wear: 95, temp: 75, realloc: 3));

        // Unhealthy (critical) + bad sectors (critical) + heavy wear (warning) + running hot (warning).
        Assert.Equal(4, alerts.Count);
        Assert.Equal(2, alerts.Count(a => a.Urgency == DiskUrgency.Critical));
        Assert.Equal(2, alerts.Count(a => a.Urgency == DiskUrgency.Warning));
    }

    [Fact]
    public void Alerts_NullSmartFields_AreNotDereferenced()
    {
        Assert.Empty(_sut.Alerts(Disk(wear: null, temp: null, realloc: null)));
    }
}
