using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public enum NetworkSortMode { Combined, Download, Upload }

/// <summary>One process row in the per-process bandwidth list. Values update in place each second.</summary>
public partial class ProcessUsageViewModel(int pid, string name) : ObservableObject
{
    public int Pid { get; } = pid;
    public int LastActiveTick { get; set; }

    [ObservableProperty] private string _name = name;
    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private double _downloadSpeed; // bytes/sec
    [ObservableProperty] private double _uploadSpeed;   // bytes/sec
    [ObservableProperty] private long _totalDown;
    [ObservableProperty] private long _totalUp;
    [ObservableProperty] private double _percent;       // share of current total throughput
    [ObservableProperty] private double _barValue;      // 0-100 relative to the busiest process
    [ObservableProperty] private Brush _levelBrush = LowBrush;
    [ObservableProperty] private bool _isHigh;

    public double Combined => DownloadSpeed + UploadSpeed;

    public string PidText => $"PID {Pid}";
    public string DownloadText => ByteFormatter.Format((long)DownloadSpeed) + "/s";
    public string UploadText => ByteFormatter.Format((long)UploadSpeed) + "/s";
    public string TotalDownText => ByteFormatter.Format(TotalDown);
    public string TotalUpText => ByteFormatter.Format(TotalUp);
    public string PercentText => Percent.ToString("0") + "%";

    partial void OnDownloadSpeedChanged(double value) => OnPropertyChanged(nameof(DownloadText));
    partial void OnUploadSpeedChanged(double value) => OnPropertyChanged(nameof(UploadText));
    partial void OnTotalDownChanged(long value) => OnPropertyChanged(nameof(TotalDownText));
    partial void OnTotalUpChanged(long value) => OnPropertyChanged(nameof(TotalUpText));
    partial void OnPercentChanged(double value) => OnPropertyChanged(nameof(PercentText));

    public static readonly Brush LowBrush = Frozen(0x00, 0xE5, 0xFF);   // cyan
    public static readonly Brush MedBrush = Frozen(0xFF, 0xD3, 0x00);   // yellow
    public static readonly Brush HighBrush = Frozen(0xFF, 0x2A, 0x6D);  // magenta/red

    public static Brush BrushFor(NetUsageLevel level) => level switch
    {
        NetUsageLevel.High => HighBrush,
        NetUsageLevel.Medium => MedBrush,
        _ => LowBrush,
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// The Net Monitor page: live per-process download/upload rates and session totals from the ETW
/// byte-accounting session, plus a rolling total-throughput sparkline. The page's Loaded/Unloaded
/// events own the session lifetime — this VM must stay the only consumer of
/// <see cref="INetworkUsageService"/> (the ETW session is a named singleton and Stop() clears counters).
/// </summary>
public partial class NetworkMonitorViewModel : ObservableObject
{
    private const int IdleTicksToPrune = 15;   // drop rows idle ~15s
    private const int ThroughputSamples = 60;  // one minute of history
    private const double ThroughputFloor = 128 * 1024; // chart max floor so an idle line doesn't fill it

    private readonly INetworkUsageService _usage;
    private readonly Dictionary<int, ProcessUsageViewModel> _items = [];
    private readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();
    private readonly Queue<double> _throughput = new();
    private Dictionary<int, ProcessNetSample> _prev = [];
    private DispatcherTimer? _monitorTimer;
    private DateTime _lastTick;
    private int _tickCount;

    public ObservableCollection<ProcessUsageViewModel> Processes { get; } = [];

    [ObservableProperty] private bool _monitoringAvailable = true;
    [ObservableProperty] private string _monitoringStatus = "Starting…";
    [ObservableProperty] private NetworkSortMode _sortMode = NetworkSortMode.Combined;
    [ObservableProperty] private bool _isSortCombined = true;
    [ObservableProperty] private bool _isSortDownload;
    [ObservableProperty] private bool _isSortUpload;

    [ObservableProperty] private IReadOnlyList<double>? _throughputHistory;
    [ObservableProperty] private double _throughputMax = ThroughputFloor;
    [ObservableProperty] private string _totalRateText = "0 B/s";
    [ObservableProperty] private string _sessionDownText = "0 B";
    [ObservableProperty] private string _sessionUpText = "0 B";
    [ObservableProperty] private string _sessionInfo = "";

    public bool MonitoringUnavailable => !MonitoringAvailable;

    public NetworkMonitorViewModel(INetworkUsageService usage)
    {
        _usage = usage;
    }

    partial void OnMonitoringAvailableChanged(bool value) => OnPropertyChanged(nameof(MonitoringUnavailable));

    partial void OnIsSortCombinedChanged(bool value) { if (value) SetSort(NetworkSortMode.Combined); }
    partial void OnIsSortDownloadChanged(bool value) { if (value) SetSort(NetworkSortMode.Download); }
    partial void OnIsSortUploadChanged(bool value) { if (value) SetSort(NetworkSortMode.Upload); }

    private void SetSort(NetworkSortMode mode)
    {
        SortMode = mode;
        ApplySort();
    }

    public void StartMonitoring()
    {
        _usage.Start();
        MonitoringAvailable = _usage.IsAvailable;
        if (!_usage.IsAvailable)
        {
            MonitoringStatus = _usage.StatusMessage;
            return;
        }

        _prev = [];
        _lastTick = DateTime.UtcNow;
        _tickCount = 0;
        _throughput.Clear();
        ThroughputHistory = null;
        ThroughputMax = ThroughputFloor;
        TotalRateText = "0 B/s";
        SessionDownText = "0 B";
        SessionUpText = "0 B";
        // Counters live only while the session runs — make it clear these aren't all-time totals.
        SessionInfo = $"Monitoring since {DateTime.Now:t} · totals reset when you leave this page";
        MonitoringStatus = "Listening for activity…";

        _monitorTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick -= OnMonitorTick;
        _monitorTimer.Tick += OnMonitorTick;
        _monitorTimer.Start();
    }

    public void StopMonitoring()
    {
        _monitorTimer?.Stop();
        _usage.Stop();
        _items.Clear();
        _prev.Clear();
        Processes.Clear();
    }

    private void OnMonitorTick(object? sender, EventArgs e)
    {
        if (!_usage.IsAvailable) return;

        var now = DateTime.UtcNow;
        double interval = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        _tickCount++;

        var snap = _usage.Snapshot();
        var result = NetRateCalculator.Compute(_prev, snap, interval);
        _prev = snap.ToDictionary(x => x.Pid);

        long sessionDown = 0, sessionUp = 0;
        foreach (var r in result.Rates)
        {
            sessionDown += r.TotalDown;
            sessionUp += r.TotalUp;

            if (r.TotalDown + r.TotalUp == 0) continue;

            if (!_items.TryGetValue(r.Pid, out var item))
            {
                item = new ProcessUsageViewModel(r.Pid, NameForPid(r.Pid));
                _items[r.Pid] = item;
                Processes.Add(item);
                ResolveIconAsync(item);
            }

            var (percent, bar) = NetRateCalculator.Share(r.Combined, result.TotalCombined, result.MaxCombined);
            item.DownloadSpeed = r.Down;
            item.UploadSpeed = r.Up;
            item.TotalDown = r.TotalDown;
            item.TotalUp = r.TotalUp;
            item.Percent = percent;
            item.BarValue = bar;

            var level = NetRateCalculator.LevelFor(r.Combined);
            item.LevelBrush = ProcessUsageViewModel.BrushFor(level);
            item.IsHigh = level == NetUsageLevel.High;

            if (r.Combined > 0) item.LastActiveTick = _tickCount;
        }

        // prune rows that have been idle a while (covers terminated/quiet processes)
        foreach (var item in _items.Values.Where(i => _tickCount - i.LastActiveTick > IdleTicksToPrune).ToList())
        {
            _items.Remove(item.Pid);
            Processes.Remove(item);
        }

        MetricsFormatter.Push(_throughput, result.TotalCombined, ThroughputSamples);
        ThroughputHistory = _throughput.ToList();
        ThroughputMax = Math.Max(ThroughputFloor, _throughput.Max() * 1.2);
        TotalRateText = MetricsFormatter.NetRate(result.TotalCombined);
        SessionDownText = ByteFormatter.Format(sessionDown);
        SessionUpText = ByteFormatter.Format(sessionUp);

        ApplySort();
        MonitoringStatus = Processes.Count == 0 ? "No active network activity." : $"{Processes.Count} active app(s)";
    }

    private void ApplySort()
    {
        if (Processes.Count < 2) return;
        IEnumerable<ProcessUsageViewModel> ordered = SortMode switch
        {
            NetworkSortMode.Download => _items.Values.OrderByDescending(i => i.DownloadSpeed).ThenByDescending(i => i.TotalDown),
            NetworkSortMode.Upload => _items.Values.OrderByDescending(i => i.UploadSpeed).ThenByDescending(i => i.TotalUp),
            _ => _items.Values.OrderByDescending(i => i.Combined).ThenByDescending(i => i.TotalDown + i.TotalUp),
        };
        var list = ordered.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            int current = Processes.IndexOf(list[i]);
            if (current >= 0 && current != i) Processes.Move(current, i);
        }
    }

    private static string NameForPid(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch (Exception) { return $"PID {pid}"; }
    }

    private void ResolveIconAsync(ProcessUsageViewModel item)
    {
        int pid = item.Pid;
        Task.Run(() =>
        {
            string? path = null;
            try { using var p = Process.GetProcessById(pid); path = p.MainModule?.FileName; }
            catch (Exception) { }
            if (string.IsNullOrEmpty(path)) return;

            var img = _iconCache.GetOrAdd(path, ExtractIcon);
            if (img is null) return;
            Application.Current?.Dispatcher.BeginInvoke(() => { if (_items.ContainsKey(pid)) item.Icon = img; });
        });
    }

    private static ImageSource? ExtractIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception) { return null; }
    }
}
