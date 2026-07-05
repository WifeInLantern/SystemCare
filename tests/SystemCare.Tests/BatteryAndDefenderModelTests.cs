using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// Pure-logic tests for the 2.4.3 models: battery wear/health is derived arithmetic (design vs.
/// full-charge capacity) and the Defender status icon is a small state map. The WMI/process code
/// that feeds these isn't unit-testable, but the math and mapping are.
/// </summary>
public class BatteryReportTests
{
    private static BatteryReport Report(long design, long full) => new()
    {
        HasBattery = true,
        DesignCapacityMilliWattHours = design,
        FullChargeCapacityMilliWattHours = full,
    };

    [Theory]
    [InlineData(100_000, 80_000, 20.0)]   // 20% worn
    [InlineData(50_000, 50_000, 0.0)]     // brand new
    [InlineData(60_000, 30_000, 50.0)]    // half gone
    public void WearPercent_IsDerivedFromDesignVsFullCharge(long design, long full, double expectedWear)
    {
        Assert.Equal(expectedWear, Report(design, full).WearPercent, precision: 3);
    }

    [Fact]
    public void WearPercent_ClampsToZero_WhenFullChargeExceedsDesign()
    {
        // New batteries often report full-charge slightly above design — wear must not go negative.
        Assert.Equal(0.0, Report(50_000, 52_000).WearPercent, precision: 3);
    }

    [Theory]
    [InlineData(0, 50_000)]      // missing design
    [InlineData(50_000, 0)]      // missing full-charge
    [InlineData(0, 0)]           // no data at all
    public void WearPercent_IsZero_WhenCapacityDataMissing(long design, long full)
    {
        Assert.Equal(0.0, Report(design, full).WearPercent, precision: 3);
    }

    [Fact]
    public void HealthPercent_IsComplementOfWear()
    {
        Assert.Equal(80.0, Report(100_000, 80_000).HealthPercent, precision: 3);
    }

    [Theory]
    [InlineData(100_000, 95_000, "Excellent")] // 5% wear -> 95
    [InlineData(100_000, 80_000, "Good")]      // 20% wear -> 80
    [InlineData(100_000, 60_000, "Fair")]      // 40% wear -> 60
    [InlineData(100_000, 40_000, "Worn")]      // 60% wear -> 40
    public void HealthBand_MapsToScoreThresholds(long design, long full, string expectedBand)
    {
        Assert.Equal(expectedBand, Report(design, full).HealthBand);
    }
}

public class DefenderStatusTests
{
    [Fact]
    public void Icon_IsCheckmark_WhenAvEnabledAndRealTimeOn()
    {
        var s = new DefenderStatus { AntivirusEnabled = true, RealTimeProtectionEnabled = true };
        Assert.Equal("ShieldCheckmark24", s.Icon);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Icon_IsPlainShield_WhenNotFullyProtected(bool av, bool rtp)
    {
        var s = new DefenderStatus { AntivirusEnabled = av, RealTimeProtectionEnabled = rtp };
        Assert.Equal("Shield24", s.Icon);
    }
}
