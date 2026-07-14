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
    ILiveMetricsService metrics, ISettingsService settings, ISnackbarService snackbar, ITrayIconService tray,
    ITemperatureService temperature)
    : IResourceAlertService
{
    // Temperatures (2.16) are sampled every 30th metrics tick (~30s): LibreHardwareMonitor reads
    // are far heavier than the snapshot fields, and thermal trends don't need 1s resolution.
    private const int TempSampleEveryTicks = 30;

    private ResourceAlertEvaluator.BreachState _cpu;
    private ResourceAlertEvaluator.BreachState _ram;
    private ResourceAlertEvaluator.BreachState _disk;
    private ResourceAlertEvaluator.BreachState _cpuTemp;
    private ResourceAlertEvaluator.BreachState _gpuTemp;
    private int _tempTick;
    private bool _tempReadInFlight;
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
        _cpuTemp = default;
        _gpuTemp = default;
        _tempTick = 0;
    }

    private void OnMetricsUpdated(object? sender, EventArgs e)
    {
        var s = settings.Current;
        if (!s.ResourceAlertsEnabled || metrics.Current is not { } snap) return;

        DateTime nowUtc = DateTime.UtcNow;

        Check("CPU", snap.CpuPercent ?? 0, s.CpuAlertThresholdPercent, s.AlertSustainedMinutes, nowUtc, ref _cpu);
        Check("Memory", snap.RamLoadPercent, s.RamAlertThresholdPercent, s.AlertSustainedMinutes, nowUtc, ref _ram);
        Check("Disk space", WorstFixedDriveUsagePercent(), s.DiskAlertThresholdPercent, s.AlertSustainedMinutes, nowUtc, ref _disk);

        // Temperature alerts (2.16): same sustained-breach logic, sampled sparsely.
        if (s.TempAlertsEnabled && ++_tempTick >= TempSampleEveryTicks)
        {
            _tempTick = 0;
            _ = SampleTemperaturesAsync();
        }
    }

    /// <summary>
    /// LibreHardwareMonitor reads can take hundreds of ms and the metrics event is raised by a
    /// DispatcherTimer (UI thread) — so the sensor read runs on the thread pool, and evaluation
    /// resumes on the captured UI context afterwards (snackbar/tray stay UI-safe). Reentrancy-guarded
    /// in case a read outlasts the sampling interval.
    /// </summary>
    private async Task SampleTemperaturesAsync()
    {
        if (_tempReadInFlight) return;
        _tempReadInFlight = true;
        try
        {
            var (cpuC, gpuC) = await Task.Run(ReadTemps);
            var s = settings.Current;
            if (!s.TempAlertsEnabled) return;
            DateTime nowUtc = DateTime.UtcNow;
            if (cpuC is double c)
                _cpuTemp = CheckTempAndAlert("CPU temperature", c, s.TempAlertCelsius, s.AlertSustainedMinutes, nowUtc, _cpuTemp);
            if (gpuC is double g)
                _gpuTemp = CheckTempAndAlert("GPU temperature", g, s.TempAlertCelsius, s.AlertSustainedMinutes, nowUtc, _gpuTemp);
        }
        catch (Exception)
        {
            // sensor hiccups must never surface; next sample retries
        }
        finally
        {
            _tempReadInFlight = false;
        }
    }

    private (double? Cpu, double? Gpu) ReadTemps()
    {
        try
        {
            double? cpu = null, gpu = null;
            foreach (var t in temperature.Read()) // never throws; [] when sensors unavailable
            {
                // TemperatureService categories are "Processor" / "Graphics" (see CategoryFor).
                if (t.Category.Equals("Processor", StringComparison.OrdinalIgnoreCase))
                    cpu = cpu is double c ? Math.Max(c, t.Celsius) : t.Celsius;
                else if (t.Category.Equals("Graphics", StringComparison.OrdinalIgnoreCase))
                    gpu = gpu is double g ? Math.Max(g, t.Celsius) : t.Celsius;
            }
            return (cpu, gpu);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private ResourceAlertEvaluator.BreachState CheckTempAndAlert(string label, double celsius, int thresholdCelsius,
        int sustainedMinutes, DateTime nowUtc, ResourceAlertEvaluator.BreachState state)
    {
        var (newState, shouldAlert) = ResourceAlertEvaluator.Evaluate(celsius, thresholdCelsius, sustainedMinutes, nowUtc, state);
        if (shouldAlert)
        {
            string title = $"{label} at {celsius:0}°C";
            string message = $"{label} has stayed at or above {thresholdCelsius}°C for {sustainedMinutes} minute(s). " +
                             "Check airflow/dust, or close heavy apps.";
            snackbar.Show(title, message, ControlAppearance.Caution, null, TimeSpan.FromSeconds(8));
            tray.ShowBalloon(title, message);
        }
        return newState;
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
