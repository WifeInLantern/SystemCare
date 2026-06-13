using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class SystemInfoViewModel : ObservableObject
{
    private const int HistorySize = 60;
    private readonly IHardwareInfoService _hardware;
    private readonly ISystemInfoService _systemInfo;
    private readonly DispatcherTimer _timer;

    private readonly Queue<double> _cpu = new();
    private readonly Queue<double> _ram = new();
    private readonly Queue<double> _net = new();

    public ObservableCollection<HardwareSpec> Specs { get; } = [];

    [ObservableProperty] private bool _isLoadingSpecs = true;
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private string _netText = "—";
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _ramHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _netHistory = [];
    [ObservableProperty] private double _netMax = 1;

    public SystemInfoViewModel(IHardwareInfoService hardware, ISystemInfoService systemInfo)
    {
        _hardware = hardware;
        _systemInfo = systemInfo;
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
            foreach (var spec in report.Specs) Specs.Add(spec);
            IsLoadingSpecs = false;
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
    }

    private static void Push(Queue<double> buffer, double value)
    {
        buffer.Enqueue(value);
        while (buffer.Count > HistorySize) buffer.Dequeue();
    }
}
