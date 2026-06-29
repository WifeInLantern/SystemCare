using System.Windows.Threading;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ISensorMonitorService
{
    IReadOnlyList<SensorReading> Current { get; }
    /// <summary>Rolling value history (oldest-first) for a sensor, keyed via <see cref="Key"/>.</summary>
    IReadOnlyList<double> History(string key);

    /// <summary>Raised on the UI thread after each successful sample.</summary>
    event EventHandler? Updated;

    /// <summary>Ref-counted; the sampler runs only while at least one consumer (the open page) is registered.</summary>
    void AddConsumer();
    void RemoveConsumer();
}

/// <summary>
/// Live source for the Sensors hub. Mirrors <see cref="LiveMetricsService"/>: one ref-counted timer, samples
/// off the UI thread via the shared <see cref="ITemperatureService"/> backend, keeps rolling per-sensor
/// history for the headline graphs, and raises <see cref="Updated"/> on the UI thread. Also fires a one-shot
/// tray balloon when a component's temperature crosses the Hot threshold (latched so it alerts once per
/// excursion). Polls only while the page is open.
/// </summary>
public sealed class SensorMonitorService : ISensorMonitorService
{
    private const int HistorySize = 60;
    private const int IntervalSeconds = 2;

    private readonly ITemperatureService _temperature;
    private readonly ITrayIconService _tray;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, Queue<double>> _history = new();
    private readonly HashSet<string> _hotLatched = new(); // components currently flagged Hot (alert once)

    private int _consumers;
    private bool _sampling;

    public SensorMonitorService(ITemperatureService temperature, ITrayIconService tray)
    {
        _temperature = temperature;
        _tray = tray;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(IntervalSeconds) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public IReadOnlyList<SensorReading> Current { get; private set; } = [];
    public event EventHandler? Updated;

    /// <summary>Stable history key for a reading: component + sensor name.</summary>
    public static string Key(SensorReading r) => $"{r.Component}|{r.Name}";

    public IReadOnlyList<double> History(string key) =>
        _history.TryGetValue(key, out var q) ? q.ToArray() : [];

    public void AddConsumer()
    {
        if (++_consumers == 1)
        {
            _ = TickAsync(); // prime so the page doesn't open blank
            _timer.Start();
        }
    }

    public void RemoveConsumer()
    {
        if (_consumers == 0) return;
        if (--_consumers == 0) _timer.Stop();
    }

    private async Task TickAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            // ReadSensors touches the kernel driver / SMART — keep it off the UI thread (we resume on it).
            var readings = await Task.Run(_temperature.ReadSensors);
            Current = readings;

            foreach (var r in readings)
            {
                string key = Key(r);
                if (!_history.TryGetValue(key, out var q)) _history[key] = q = new Queue<double>();
                MetricsFormatter.Push(q, r.Value, HistorySize);
            }

            CheckTempAlerts(readings);
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // a bad sample must never tear down the timer
        }
        finally
        {
            _sampling = false;
        }
    }

    private void CheckTempAlerts(IReadOnlyList<SensorReading> readings)
    {
        foreach (var group in readings.Where(r => r.Kind == SensorKind.Temperature).GroupBy(r => r.Component))
        {
            double max = group.Max(r => r.Value);
            if (max >= SensorFormatting.HotC)
            {
                if (_hotLatched.Add(group.Key)) // first tick above Hot for this component
                    _tray.ShowBalloon("High temperature", $"{group.Key} is {Math.Round(max)} °C");
            }
            else
            {
                _hotLatched.Remove(group.Key); // cooled down — re-arm
            }
        }
    }
}
