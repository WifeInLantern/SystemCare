using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

/// <summary>One hardware row plus its live temperature (updated on the page's timer).</summary>
public partial class HardwareSpecViewModel(HardwareSpec spec) : ObservableObject
{
    public HardwareSpec Spec { get; } = spec;
    public string Category => Spec.Category;
    public string Name => Spec.Name;
    public string Detail => Spec.Detail;
    public string Icon => Spec.Icon;

    [ObservableProperty] private bool _hasTemperature;
    [ObservableProperty] private string _temperatureText = "";
    [ObservableProperty] private string _temperatureColor = "#00E5FF";

    public void SetTemperature(double celsius)
    {
        TemperatureText = $"{celsius:0}°C";
        TemperatureColor = celsius >= 75 ? "#FF4D4F" : celsius >= 60 ? "#FFC400" : "#00E5FF";
        HasTemperature = true;
    }
}

public partial class SystemInfoViewModel : ObservableObject
{
    private const int HistorySize = 60;
    private readonly IHardwareInfoService _hardware;
    private readonly ISystemInfoService _systemInfo;
    private readonly ITemperatureService _temperatures;
    private readonly DispatcherTimer _timer;

    private readonly Queue<double> _cpu = new();
    private readonly Queue<double> _ram = new();
    private readonly Queue<double> _net = new();
    private int _tick;
    private bool _readingTemps;

    public ObservableCollection<HardwareSpecViewModel> Specs { get; } = [];

    [ObservableProperty] private bool _isLoadingSpecs = true;
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private string _netText = "—";
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _ramHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _netHistory = [];
    [ObservableProperty] private double _netMax = 1;

    public SystemInfoViewModel(IHardwareInfoService hardware, ISystemInfoService systemInfo, ITemperatureService temperatures)
    {
        _hardware = hardware;
        _systemInfo = systemInfo;
        _temperatures = temperatures;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public async void OnNavigatedTo()
    {
        Tick();
        _timer.Start();
        if (Specs.Count == 0)
        {
            IsLoadingSpecs = true;
            var report = await _hardware.GetReportAsync();
            Specs.Clear();
            foreach (var spec in report.Specs) Specs.Add(new HardwareSpecViewModel(spec));
            IsLoadingSpecs = false;
            _ = RefreshTemperaturesAsync();
        }
    }

    public void OnNavigatedFrom() => _timer.Stop();

    private void Tick()
    {
        var s = _systemInfo.GetSnapshot(includeDrives: false);
        double cpu = s.CpuPercent ?? 0;
        double netTotal = (s.NetRecvBytesPerSec + s.NetSentBytesPerSec);

        CpuText = s.CpuPercent is double c ? $"{c:0}%" : "—";
        RamText = $"{s.RamLoadPercent:0}%";
        NetText = $"↓ {ByteFormatter.Format((long)s.NetRecvBytesPerSec)}/s   ↑ {ByteFormatter.Format((long)s.NetSentBytesPerSec)}/s";

        Push(_cpu, cpu);
        Push(_ram, s.RamLoadPercent);
        Push(_net, netTotal);

        CpuHistory = _cpu.ToArray();
        RamHistory = _ram.ToArray();
        NetHistory = _net.ToArray();
        NetMax = Math.Max(1, _net.Count == 0 ? 1 : _net.Max());

        // Sensor reads touch a kernel driver + SMART, so refresh temps every 2s, off the UI thread.
        if (Specs.Count > 0 && _tick++ % 2 == 0) _ = RefreshTemperaturesAsync();
    }

    private async Task RefreshTemperaturesAsync()
    {
        if (_readingTemps) return;
        _readingTemps = true;
        try
        {
            var temps = await Task.Run(_temperatures.Read);
            if (temps.Count == 0) return;

            var pool = temps.ToList();
            foreach (var row in Specs)
            {
                int i = BestMatchIndex(row, pool);
                if (i < 0) continue;
                row.SetTemperature(pool[i].Celsius);
                pool.RemoveAt(i); // each reading maps to at most one row
            }
        }
        finally
        {
            _readingTemps = false;
        }
    }

    /// <summary>Index of the temperature that best fits this row: same category, then most name overlap.</summary>
    private static int BestMatchIndex(HardwareSpecViewModel row, List<ComponentTemperature> pool)
    {
        int best = -1, bestScore = -1;
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].Category.Equals(row.Category, StringComparison.OrdinalIgnoreCase)) continue;
            int score = NameOverlap(row.Name, pool[i].HardwareName); // 0 is a valid fallback for single-of-a-kind
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    private static int NameOverlap(string a, string b)
    {
        var tb = Tokenize(b);
        return Tokenize(a).Count(tb.Contains);
    }

    private static HashSet<string> Tokenize(string s) =>
        s.ToLowerInvariant()
            .Split([' ', '-', '_', '(', ')', '.', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToHashSet();

    private static void Push(Queue<double> buffer, double value)
    {
        buffer.Enqueue(value);
        while (buffer.Count > HistorySize) buffer.Dequeue();
    }
}
