using SystemCare.Models;

namespace SystemCare.Services;

public interface IDiskHealthScoreService
{
    /// <summary>Composite 0–100 health score for one drive.</summary>
    int Score(PhysicalDiskHealth disk);
    string Band(int score);
    /// <summary>Predictive/health alerts for one drive (may be empty).</summary>
    IReadOnlyList<DiskAlert> Alerts(PhysicalDiskHealth disk);
}

/// <summary>
/// Penalty-based per-drive scoring + predictive alerts, in the same spirit as
/// <see cref="HealthScoreService"/>. Deliberately conservative: only genuinely meaningful signals
/// (failing SMART, uncorrectable errors, high wear/temperature) reduce the score or raise alerts —
/// cumulative *corrected* read/write totals are shown for context but never scored, to avoid false alarms.
/// </summary>
public class DiskHealthScoreService : IDiskHealthScoreService
{
    public int Score(PhysicalDiskHealth d)
    {
        double score = 100;

        // SMART verdict caps the ceiling.
        if (d.Health == DiskHealthStatus.Warning) score = Math.Min(score, 60);
        if (d.Health == DiskHealthStatus.Unhealthy) score = Math.Min(score, 20);

        // SSD wear: 0% used = none, 100% used = end of rated life.
        if (d.WearPercent is int w && w > 0) score -= w * 0.5;

        // Temperature.
        if (d.TemperatureC is double t)
        {
            if (t >= 70) score -= 25;
            else if (t >= 60) score -= 10;
        }

        // Uncorrectable errors / bad sectors are a hard red flag.
        if ((d.ReallocatedSectors ?? 0) > 0) score = Math.Min(score, 35);

        return (int)Math.Clamp(score, 0, 100);
    }

    public string Band(int score) => score switch
    {
        < 0 => "Not scored",
        >= 90 => "Excellent",
        >= 70 => "Good",
        >= 40 => "Needs attention",
        _ => "At risk",
    };

    public IReadOnlyList<DiskAlert> Alerts(PhysicalDiskHealth d)
    {
        var list = new List<DiskAlert>();

        if (d.Health == DiskHealthStatus.Unhealthy)
            list.Add(new DiskAlert
            {
                Urgency = DiskUrgency.Critical,
                Title = $"{d.Name} may be failing",
                Detail = "SMART reports this drive is unhealthy. Back up important files now.",
                ActionLabel = "Create restore point", ActionTarget = "__restorepoint",
            });

        if ((d.ReallocatedSectors ?? 0) > 0)
            list.Add(new DiskAlert
            {
                Urgency = DiskUrgency.Critical,
                Title = $"Bad sectors on {d.Name}",
                Detail = $"{d.ReallocatedSectors} uncorrectable error(s) — a sign of physical wear. Back up soon.",
                ActionLabel = "Create restore point", ActionTarget = "__restorepoint",
            });

        if (d.Health == DiskHealthStatus.Warning)
            list.Add(new DiskAlert
            {
                Urgency = DiskUrgency.Warning,
                Title = $"{d.Name} health warning",
                Detail = "SMART flagged a warning. Run maintenance and keep a current backup.",
                ActionLabel = "Optimize drive", ActionTarget = "__maintain",
            });

        if (d.WearPercent is int w)
        {
            if (w >= 90)
                list.Add(new DiskAlert
                {
                    Urgency = DiskUrgency.Warning,
                    Title = $"{d.Name} is heavily worn",
                    Detail = $"SSD wear at {w}%. Plan to replace it and keep backups current.",
                });
            else if (w >= 80)
                list.Add(new DiskAlert
                {
                    Urgency = DiskUrgency.Caution,
                    Title = $"{d.Name} is aging",
                    Detail = $"SSD wear at {w}%. Reduce unnecessary writes (e.g. clear junk) and back up regularly.",
                    ActionLabel = "Free up space", ActionTarget = "Cleanup",
                });
        }

        if (d.TemperatureC is double t)
        {
            if (t >= 70)
                list.Add(new DiskAlert
                {
                    Urgency = DiskUrgency.Warning,
                    Title = $"{d.Name} is running hot",
                    Detail = $"{t:0}°C. Improve airflow — sustained heat shortens drive life.",
                });
            else if (t >= 60)
                list.Add(new DiskAlert
                {
                    Urgency = DiskUrgency.Caution,
                    Title = $"{d.Name} is warm",
                    Detail = $"{t:0}°C. Keep an eye on temperatures and ensure good ventilation.",
                });
        }

        return list;
    }
}
