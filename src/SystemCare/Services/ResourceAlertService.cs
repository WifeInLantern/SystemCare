using SystemCare.Helpers;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.Services;

public interface IResourceAlertService
{
    /// <summary>Begins watching <see cref="ILiveMetricsService"/> for sustained-threshold breaches. Idempotent.</summary>
    void Start();
    /// <summary>Stops watching and clears any in-progress breach timers. Idempotent.</summary>
    void Stop();
}

/// <summary>
/// Background watcher: subscribes to <see cref="ILiveMetricsService"/>'s existing 1s sampler (the same
/// AddConsumer/Updated/RemoveConsumer lifecycle <c>TrayIconService</c> and <c>MiniMonitorService</c> already
/// use) and raises a toast + tray balloon the first time CPU, RAM, or disk-space usage stays above a
/// user-configured threshold for a sustained duration. Disk usage is read directly via <see cref="DriveInfo"/>
/// rather than from the snapshot, because <c>LiveMetricsService</c> samples with <c>includeDrives: false</c>
/// for performance and never populates <c>SystemSnapshot.Drives</c>.
/// </summary>
public sealed class ResourceAlertService(
    ILiveMetricsService metrics, ISettingsService settings, ISnackbarService snackbar, ITrayIconService tray)
    : IResourceAlertService
{
    private ResourceAlertEvaluator.BreachState _cpu;
    private ResourceAlertEvaluator.BreachState _ram;
    private ResourceAlertEvaluator.BreachState _disk;
    private bool _started;

    public void Start()
    {
        if (_started) return;
        _started = true;
        metrics.AddConsumer();
        metrics.Updated += OnMetricsUpdated;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        metrics.Updated -= OnMetricsUpdated;
        metrics.RemoveConsumer();
        _cpu = default;
        _ram = default;
        _disk = default;
    }

    private void OnMetricsUpdated(object? sender, EventArgs e)
    {
        var s = settings.Current;
        if (!s.ResourceAlertsEnabled || metrics.Current is not { } snap) return;

        DateTime nowUtc = DateTime.UtcNow;

        Check("CPU", snap.CpuPercent ?? 0, s.CpuAlertThresholdPercent, s.AlertSustainedMinutes, nowUtc, ref _cpu);
        Check("Memory", snap.RamLoadPercent, s.RamAlertThresholdPercent, s.AlertSustainedMinutes, nowUtc, ref _ram);
        Check("Disk space", WorstFixedDriveUsagePercent(), s.DiskAlertThresholdPercent, s.AlertSustainedMinutes, nowUtc, ref _disk);
    }

    private void Check(string label, double currentValue, int thresholdPercent, int sustainedMinutes,
        DateTime nowUtc, ref ResourceAlertEvaluator.BreachState state)
    {
        var (newState, shouldAlert) = ResourceAlertEvaluator.Evaluate(currentValue, thresholdPercent, sustainedMinutes, nowUtc, state);
        state = newState;
        if (shouldAlert) RaiseAlert(label, currentValue, thresholdPercent, sustainedMinutes);
    }

    private void RaiseAlert(string label, double currentValue, int thresholdPercent, int sustainedMinutes)
    {
        string title = $"{label} pegged at {currentValue:0}%+";
        string message = $"{label} usage has stayed above {thresholdPercent}% for {sustainedMinutes} minute(s).";
        snackbar.Show(title, message, ControlAppearance.Caution, null, TimeSpan.FromSeconds(8));
        tray.ShowBalloon(title, message);
    }

    private static double WorstFixedDriveUsagePercent()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => d.TotalSize > 0 ? (d.TotalSize - d.AvailableFreeSpace) * 100.0 / d.TotalSize : 0)
                .DefaultIfEmpty(0)
                .Max();
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
