using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public enum NetworkSortMode { Combined, Download, Upload }

/// <summary>One process row in the per-process bandwidth panel. Values update in place each second.</summary>
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

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public partial class NetworkToolsViewModel : ObservableObject
{
    private const int MaxOutputChars = 40_000;
    private const long MedThreshold = 256 * 1024;          // 256 KB/s
    private const long HighThreshold = 2L * 1024 * 1024;   // 2 MB/s
    private const int IdleTicksToPrune = 15;               // drop rows idle ~15s

    private readonly INetworkToolsService _network;
    private readonly INetworkUsageService _usage;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    // per-process monitoring state
    private readonly Dictionary<int, ProcessUsageViewModel> _items = [];
    private readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();
    private Dictionary<int, ProcessNetSample> _prev = [];
    private DispatcherTimer? _monitorTimer;
    private DateTime _lastTick;
    private int _tickCount;

    public ObservableCollection<NetConnection> Connections { get; } = [];
    public ObservableCollection<ProcessUsageViewModel> Processes { get; } = [];

    [ObservableProperty] private bool _isLoadingConnections;
    [ObservableProperty] private string _connectionSummary = "";
    [ObservableProperty] private string _target = "8.8.8.8";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _outputText = "Run a tool to see output here.";

    // bandwidth panel
    [ObservableProperty] private bool _monitoringAvailable = true;
    [ObservableProperty] private string _monitoringStatus = "Starting…";
    [ObservableProperty] private NetworkSortMode _sortMode = NetworkSortMode.Combined;
    [ObservableProperty] private bool _isSortCombined = true;
    [ObservableProperty] private bool _isSortDownload;
    [ObservableProperty] private bool _isSortUpload;

    public bool MonitoringUnavailable => !MonitoringAvailable;

    public NetworkToolsViewModel(INetworkToolsService network, INetworkUsageService usage)
    {
        _network = network;
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

    public async void OnNavigatedTo()
    {
        if (Connections.Count == 0) await RefreshConnectionsAsync();
    }

    // ---------------- per-process bandwidth monitoring ----------------

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
        double interval = Math.Max(0.25, (now - _lastTick).TotalSeconds);
        _lastTick = now;
        _tickCount++;

        var snap = _usage.Snapshot();
        double totalCombined = 0, maxCombined = 0;
        var speeds = new Dictionary<int, (double Down, double Up, long TDown, long TUp)>(snap.Count);

        foreach (var s in snap)
        {
            long pSent = 0, pRecv = 0;
            if (_prev.TryGetValue(s.Pid, out var prev)) { pSent = prev.SentBytes; pRecv = prev.RecvBytes; }
            double down = Math.Max(0, (s.RecvBytes - pRecv) / interval);
            double up = Math.Max(0, (s.SentBytes - pSent) / interval);
            speeds[s.Pid] = (down, up, s.RecvBytes, s.SentBytes);
            double combined = down + up;
            totalCombined += combined;
            if (combined > maxCombined) maxCombined = combined;
        }
        _prev = snap.ToDictionary(x => x.Pid);

        foreach (var (pid, v) in speeds)
        {
            if (v.TDown + v.TUp == 0) continue;
            double combined = v.Down + v.Up;

            if (!_items.TryGetValue(pid, out var item))
            {
                item = new ProcessUsageViewModel(pid, NameForPid(pid));
                _items[pid] = item;
                Processes.Add(item);
                ResolveIconAsync(item);
            }

            item.DownloadSpeed = v.Down;
            item.UploadSpeed = v.Up;
            item.TotalDown = v.TDown;
            item.TotalUp = v.TUp;
            item.Percent = totalCombined > 0 ? combined / totalCombined * 100 : 0;
            item.BarValue = maxCombined > 0 ? combined / maxCombined * 100 : 0;

            if (combined >= HighThreshold) { item.LevelBrush = ProcessUsageViewModel.HighBrush; item.IsHigh = true; }
            else if (combined >= MedThreshold) { item.LevelBrush = ProcessUsageViewModel.MedBrush; item.IsHigh = false; }
            else { item.LevelBrush = ProcessUsageViewModel.LowBrush; item.IsHigh = false; }

            if (combined > 0) item.LastActiveTick = _tickCount;
        }

        // prune rows that have been idle a while (covers terminated/quiet processes)
        foreach (var item in _items.Values.Where(i => _tickCount - i.LastActiveTick > IdleTicksToPrune).ToList())
        {
            _items.Remove(item.Pid);
            Processes.Remove(item);
        }

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

    // ---------------- connections + tools (unchanged) ----------------

    [RelayCommand]
    private async Task RefreshConnectionsAsync()
    {
        IsLoadingConnections = true;
        try
        {
            var list = await Task.Run(_network.GetConnections);
            Connections.Clear();
            foreach (var c in list) Connections.Add(c);
            ConnectionSummary = $"{list.Count} active connections";
        }
        finally
        {
            IsLoadingConnections = false;
        }
    }

    [RelayCommand]
    private async Task PingAsync() => await RunToolAsync(ct => _network.PingAsync(Target, AppendLine, ct));

    [RelayCommand]
    private async Task TracerouteAsync() => await RunToolAsync(ct => _network.TracerouteAsync(Target, AppendLine, ct));

    [RelayCommand]
    private void FlushDns()
    {
        _output.Clear();
        AppendLine(_network.FlushDns());
        FlushOutput();
    }

    [RelayCommand]
    private async Task RenewIpAsync() => await RunToolAsync(ct => _network.RenewIpAsync(AppendLine, ct));

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunToolAsync(Func<CancellationToken, Task> action)
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _output.Clear();
        OutputText = "";
        try
        {
            await action(_cts.Token);
        }
        catch (Exception ex)
        {
            AppendLine($"Error: {ex.Message}");
        }
        finally
        {
            FlushOutput();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void AppendLine(string line)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _output.AppendLine(line);
            if (_output.Length > MaxOutputChars) _output.Remove(0, _output.Length - MaxOutputChars);
            OutputText = _output.ToString();
        });
    }

    private void FlushOutput() =>
        Application.Current?.Dispatcher.Invoke(() => OutputText = _output.ToString());
}
