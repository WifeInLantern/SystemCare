using SystemCare.Models;

namespace SystemCare.Helpers;

public enum TempSeverity { Normal, Warm, Hot }

/// <summary>
/// Pure presentation helpers for the Sensors hub: per-kind units, value formatting, the section label,
/// and temperature severity against tunable thresholds. Dependency-free so it can be unit-tested; the
/// reading itself is interop (<c>TemperatureService.ReadSensors</c>).
/// </summary>
public static class SensorFormatting
{
    // Temperature severity thresholds in °C (tunable).
    public const double WarmC = 80.0;
    public const double HotC = 90.0;

    public static string Unit(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Fan => "RPM",
        SensorKind.Voltage => "V",
        SensorKind.Clock => "MHz",
        SensorKind.Load => "%",
        SensorKind.Power => "W",
        SensorKind.Control => "%",
        _ => "",
    };

    public static string Format(SensorKind kind, double value) => kind switch
    {
        SensorKind.Temperature => $"{Math.Round(value)} °C",
        SensorKind.Fan => $"{Math.Round(value)} RPM",
        SensorKind.Voltage => $"{value:0.000} V",
        SensorKind.Clock => $"{Math.Round(value)} MHz",
        SensorKind.Load => $"{Math.Round(value)} %",
        SensorKind.Power => $"{value:0.0} W",
        SensorKind.Control => $"{Math.Round(value)} %",
        _ => value.ToString("0.##"),
    };

    /// <summary>Plural section header for a group of one kind.</summary>
    public static string KindLabel(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "Temperatures",
        SensorKind.Fan => "Fans",
        SensorKind.Voltage => "Voltages",
        SensorKind.Clock => "Clocks",
        SensorKind.Load => "Load",
        SensorKind.Power => "Power",
        SensorKind.Control => "Fan control",
        _ => "",
    };

    public static TempSeverity Severity(double celsius) =>
        celsius >= HotC ? TempSeverity.Hot
        : celsius >= WarmC ? TempSeverity.Warm
        : TempSeverity.Normal;
}
