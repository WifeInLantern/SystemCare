namespace SystemCare.Models;

/// <summary>
/// Battery health snapshot. Capacities are in mWh. Wear is derived from
/// design vs. full-charge capacity: a battery that can no longer hold its
/// original charge has worn down.
/// </summary>
public class BatteryReport
{
    /// <summary>False on desktops / machines with no battery — the page shows an empty state.</summary>
    public bool HasBattery { get; init; }

    public string Name { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Chemistry { get; init; } = "";

    public long DesignCapacityMilliWattHours { get; init; }
    public long FullChargeCapacityMilliWattHours { get; init; }
    public int CycleCount { get; init; }

    /// <summary>Current charge as a percentage (0–100), or -1 if unknown.</summary>
    public int ChargePercent { get; init; } = -1;

    /// <summary>True when running on AC power.</summary>
    public bool OnAcPower { get; init; }

    /// <summary>
    /// Wear as a percentage 0–100. 0 % means full-charge capacity still equals the
    /// original design capacity; higher means more degradation.
    /// </summary>
    public double WearPercent
    {
        get
        {
            if (DesignCapacityMilliWattHours <= 0 || FullChargeCapacityMilliWattHours <= 0) return 0;
            double wear = (1.0 - (double)FullChargeCapacityMilliWattHours / DesignCapacityMilliWattHours) * 100.0;
            return Math.Clamp(wear, 0, 100);
        }
    }

    /// <summary>Remaining health as a 0–100 score, suitable for the HealthGauge control.</summary>
    public double HealthPercent => Math.Round(100.0 - WearPercent);

    /// <summary>A short verdict based on the health score.</summary>
    public string HealthBand => HealthPercent switch
    {
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= 50 => "Fair",
        _ => "Worn",
    };
}
