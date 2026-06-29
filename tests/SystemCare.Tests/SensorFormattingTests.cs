using SystemCare.Helpers;
using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// The pure Sensors-hub presentation helpers: per-kind units, value formatting precision, and the
/// temperature severity thresholds. The sensor reading itself is interop (verified live).
/// </summary>
public class SensorFormattingTests
{
    [Theory]
    [InlineData(SensorKind.Temperature, "°C")]
    [InlineData(SensorKind.Fan, "RPM")]
    [InlineData(SensorKind.Voltage, "V")]
    [InlineData(SensorKind.Clock, "MHz")]
    [InlineData(SensorKind.Load, "%")]
    [InlineData(SensorKind.Power, "W")]
    [InlineData(SensorKind.Control, "%")]
    public void Unit_MapsEachKind(SensorKind kind, string expected)
    {
        Assert.Equal(expected, SensorFormatting.Unit(kind));
    }

    [Fact]
    public void Format_RoundsTemperatureToWhole()
    {
        Assert.Equal("64 °C", SensorFormatting.Format(SensorKind.Temperature, 63.7));
    }

    [Fact]
    public void Format_VoltageKeepsThreeDecimals()
    {
        Assert.Equal("1.224 V", SensorFormatting.Format(SensorKind.Voltage, 1.2237));
    }

    [Fact]
    public void Format_PowerKeepsOneDecimal()
    {
        Assert.Equal("65.4 W", SensorFormatting.Format(SensorKind.Power, 65.41));
    }

    [Theory]
    [InlineData(40, TempSeverity.Normal)]
    [InlineData(79.9, TempSeverity.Normal)]
    [InlineData(80, TempSeverity.Warm)]    // Warm boundary inclusive
    [InlineData(89.9, TempSeverity.Warm)]
    [InlineData(90, TempSeverity.Hot)]     // Hot boundary inclusive
    [InlineData(105, TempSeverity.Hot)]
    public void Severity_ClassifiesAgainstThresholds(double celsius, TempSeverity expected)
    {
        Assert.Equal(expected, SensorFormatting.Severity(celsius));
    }

    [Fact]
    public void KindLabel_IsPluralHeader()
    {
        Assert.Equal("Temperatures", SensorFormatting.KindLabel(SensorKind.Temperature));
        Assert.Equal("Fans", SensorFormatting.KindLabel(SensorKind.Fan));
    }
}
