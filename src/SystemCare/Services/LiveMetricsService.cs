using System.Windows.Threading;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ILiveMetricsService
{
    SystemSnapshot? Current { get; }
    IReadOnlyList<double> CpuHistory { get; }
    IReadOnlyList<double> RamHistory { get; }
    IReadOnlyList<double> NetHistory { get; }
    IReadOnlyList<ComponentTemperature> Temperatures { get; }

    /// <summary>Raised on the UI thread after each successful sample.</summary>
    event EventHandler? Updated;

    /// <summary>Register interest; the 1s sampler runs only while at least one consumer is registered.</summary>
    void AddConsumer();
    void RemoveConsumer();
}

/// <summary>
/// Single always-available source of live system metrics for the tray monitor and mini-widget. Owns one
/// 1-second timer and a <b>private</b> <see cref="SystemInfoService"/> instance so its delta state (CPU%,
/// network rates) can never collide with the per-page samplers in DashboardViewModel / SystemInfoViewModel.
/// Sampling runs only while at least one consumer is registered (ref-counted), so nothing polls unless the
/// tray stats or widget are switched on.
/// </summary>
public sealed class LiveMetricsService : ILiveMetricsService
{
    private const int HistorySize = 60;
    private const int TempEveryNTicks = 4; // temperatures touch a kernel driver — sample them less often

    private readonly ITemperatureService _temperature;
    private readonly SystemInfoService _sampler = new(); // isolated delta state, independent of the DI singleton
    private readonly DispatcherTimer _timer;

    private readonly Queue<double> _cpu = new();
    private readonly Queue<double> _ram = new();
    private readonly Queue<double> _net = new();

    private int _consumers;
    private int _tick;
    private bool _sampling; // re-entrancy guard: skip a tick if the previous sample is still running
    private bool _tempBusy;

    public LiveMetricsService(ITemperatureService temperature)
    {
        _temperature = temperature;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public SystemSnapshot? Current { get; private set; }
    public IReadOnlyList<double> CpuHistory { get; private set; } = [];
    public IReadOnlyList<double> RamHistory { get; private set; } = [];
    public IReadOnlyList<double> NetHistory { get; private set; } = [];
    public IReadOnlyList<ComponentTemperature> Temperatures { get; private set; } = [];

    public event EventHandler? Updated;

    public void AddConsumer()
    {
        if (++_consumers == 1)
        {
            _ = TickAsync(); // prime so consumers don't start blank
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
            // The snapshot touches GetSystemTimes/NIC stats — keep it off the UI thread (we resume on it).
            var snapshot = await Task.Run(() => _sampler.GetSnapshot(includeDrives: false));
            Current = snapshot;

            MetricsFormatter.Push(_cpu, snapshot.CpuPercent ?? 0, HistorySize);
            MetricsFormatter.Push(_ram, snapshot.RamLoadPercent, HistorySize);
            MetricsFormatter.Push(_net, snapshot.NetRecvBytesPerSec + snapshot.NetSentBytesPerSec, HistorySize);
            CpuHistory = _cpu.ToArray();
            RamHistory = _ram.ToArray();
            NetHistory = _net.ToArray();

            // Temperatures are slow; refresh on a slower cadence on a worker thread. The result is picked up
            // by the next Updated tick (a sub-second lag on the temp row is fine).
            if (_tick++ % TempEveryNTicks == 0 && !_tempBusy)
            {
                _tempBusy = true;
                _ = Task.Run(() =>
                {
                    try { Temperatures = _temperature.Read(); }
                    catch (Exception) { /* sensors unavailable — leave the previous values */ }
                    finally { _tempBusy = false; }
                });
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // A bad sample must never tear down the timer.
        }
        finally
        {
            _sampling = false;
        }
    }
}
