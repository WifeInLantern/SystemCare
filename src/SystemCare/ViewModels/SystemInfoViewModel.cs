using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
    public string? Tooltip => Spec.Tooltip;
    public string Health => Spec.Health ?? "";
    public bool HasHealth => !string.IsNullOrWhiteSpace(Spec.Health);

    public string HealthBrush => !HasHealth ? "#00E5FF"
        : Spec.Health!.Contains("fail", StringComparison.OrdinalIgnoreCase)
          || Spec.Health.Contains("degrad", StringComparison.OrdinalIgnoreCase)
          || Spec.Health.Contains("bad", StringComparison.OrdinalIgnoreCase) ? "#FF4D4F"
        : Spec.Health.Contains("healthy", StringComparison.OrdinalIgnoreCase)
          || Spec.Health.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "#00FFA3"
        : "#00E5FF";

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

/// <summary>A storage volume's live capacity, updated in place on a slow cadence (no flicker).</summary>
public partial class DriveViewModel(string name, string label) : ObservableObject
{
    public string Name { get; } = name;
    public string Label { get; } = label;
    [ObservableProperty] private double _usedPercent;
    [ObservableProperty] private string _capacityText = "";
}

/// <summary>A collapsible panel section (CPU, GPU, …) holding spec rows and optional drive bars.</summary>
public partial class SpecSectionViewModel(string header, string icon, bool isExpanded = true) : ObservableObject
{
    public string Header { get; } = header;
    public string Icon { get; } = icon;
    public bool IsExpanded { get; } = isExpanded;
    public ObservableCollection<HardwareSpecViewModel> Items { get; } = [];
    public ObservableCollection<DriveViewModel> Drives { get; } = [];

    [ObservableProperty] private bool _hasDrives;
}

public partial class SystemInfoViewModel : ObservableObject
{
    private const int HistorySize = 60;
    private readonly IHardwareInfoService _hardware;
    private readonly ISystemInfoService _systemInfo;
    private readonly ITemperatureService _temperatures;
    private readonly IBootPerformanceService _boot;
    private readonly DispatcherTimer _timer;

    private readonly Queue<double> _cpu = new();
    private readonly Queue<double> _ram = new();
    private readonly Queue<double> _net = new();
    private readonly List<HardwareSpecViewModel> _allRows = [];
    private SpecSectionViewModel? _storage;
    private int _tempTick;
    private int _driveTick;
    private bool _readingTemps;
    private bool _readingSnapshot;

    public ObservableCollection<SpecSectionViewModel> Sections { get; } = [];

    [ObservableProperty] private bool _isLoadingSpecs = true;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private string _cpuSubText = "";
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private string _ramSubText = "";
    [ObservableProperty] private string _netText = "—";
    [ObservableProperty] private string _netSubText = "";
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _ramHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _netHistory = [];
    [ObservableProperty] private double _netMax = 1;

    public SystemInfoViewModel(IHardwareInfoService hardware, ISystemInfoService systemInfo,
        ITemperatureService temperatures, IBootPerformanceService boot)
    {
        _hardware = hardware;
        _systemInfo = systemInfo;
        _temperatures = temperatures;
        _boot = boot;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public async void OnNavigatedTo()
    {
        try
        {
            await TickAsync();
            _timer.Start();
            if (Sections.Count == 0)
            {
                IsLoadingSpecs = true;
                var report = await _hardware.GetReportAsync();
                BuildSections(report);
                CpuSubText = _allRows.FirstOrDefault(r => r.Category == "Processor")?.Detail ?? "";
                await LoadNetworkAsync();
                await LoadSummaryAsync();
                var snap = await Task.Run(() => _systemInfo.GetSnapshot(includeDrives: true));
                UpdateDrives(snap.Drives);
                IsLoadingSpecs = false;
                _ = RefreshTemperaturesAsync();
            }
        }
        catch (Exception)
        {
            IsLoadingSpecs = false; // never let a hardware sweep crash the page
        }
    }

    public void OnNavigatedFrom() => _timer.Stop();

    private void BuildSections(HardwareReport report)
    {
        Sections.Clear();
        _allRows.Clear();

        AddSection("Processor", "DeveloperBoard24", true, report, HardwareSection.Cpu);
        AddSection("Graphics", "VideoClip24", true, report, HardwareSection.Gpu);
        AddSection("Memory", "Memory16", true, report, HardwareSection.Ram);

        // Storage carries both the physical-disk rows and the live volume-capacity bars.
        _storage = new SpecSectionViewModel("Storage", "HardDrive24");
        foreach (var sp in report.Specs.Where(s => s.Section == HardwareSection.Storage))
        {
            var row = new HardwareSpecViewModel(sp);
            _storage.Items.Add(row);
            _allRows.Add(row);
        }
        Sections.Add(_storage);

        AddSection("Operating system", "Desktop24", false, report,
            HardwareSection.Os, HardwareSection.Board, HardwareSection.Battery);
    }

    private void AddSection(string header, string icon, bool isExpanded, HardwareReport report, params HardwareSection[] sections)
    {
        var specs = report.Specs.Where(s => sections.Contains(s.Section)).ToList();
        if (specs.Count == 0) return;
        var section = new SpecSectionViewModel(header, icon, isExpanded);
        foreach (var sp in specs)
        {
            var row = new HardwareSpecViewModel(sp);
            section.Items.Add(row);
            _allRows.Add(row);
        }
        Sections.Add(section);
    }

    private async Task LoadSummaryAsync()
    {
        try
        {
            var report = await _boot.GetAsync();
            string os = _allRows.FirstOrDefault(r => r.Category == "Operating system")?.Name ?? "Windows";
            SummaryText = TextHelpers.JoinParts(Environment.MachineName, os,
                $"up {report.UptimeText}", $"since {report.LastBootText}");
        }
        catch (Exception) { /* uptime is best-effort */ }
    }

    private async Task LoadNetworkAsync()
    {
        var rows = await Task.Run(ReadNetworkAdapters);
        if (rows.Count == 0) return;

        var section = new SpecSectionViewModel("Network", "Globe24", isExpanded: false);
        foreach (var sp in rows) section.Items.Add(new HardwareSpecViewModel(sp));
        Sections.Add(section);

        var primary = rows[0];
        NetSubText = TextHelpers.JoinParts(primary.Name, primary.Detail);
    }

    private static List<HardwareSpec> ReadNetworkAdapters()
    {
        var list = new List<HardwareSpec>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                string type = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
                    _ => nic.NetworkInterfaceType.ToString(),
                };
                string? ipv4 = null;
                try
                {
                    ipv4 = nic.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
                }
                catch (Exception) { }

                list.Add(new HardwareSpec
                {
                    Category = "Network", Section = HardwareSection.Network, Icon = "Globe24",
                    Name = string.IsNullOrWhiteSpace(nic.Name) ? nic.Description : nic.Name,
                    Detail = TextHelpers.JoinParts(type, FormatLinkSpeed(nic.Speed), ipv4),
                    Tooltip = TextHelpers.JoinParts(nic.Description, nic.GetPhysicalAddress().ToString()),
                });
            }
        }
        catch (Exception) { }
        return list;
    }

    private static string? FormatLinkSpeed(long bitsPerSec)
    {
        if (bitsPerSec <= 0) return null;
        double mbps = bitsPerSec / 1_000_000.0;
        return mbps >= 1000 ? $"{mbps / 1000.0:0.#} Gbps" : $"{mbps:0} Mbps";
    }

    private async Task TickAsync()
    {
        if (_readingSnapshot) return;
        _readingSnapshot = true;
        try
        {
            bool wantDrives = _driveTick++ % 10 == 0; // capacity changes slowly — refresh ~every 10s
            // NIC + drive enumeration off the UI thread so the panel never stutters.
            var s = await Task.Run(() => _systemInfo.GetSnapshot(includeDrives: wantDrives));

            double cpu = s.CpuPercent ?? 0;
            double netTotal = s.NetRecvBytesPerSec + s.NetSentBytesPerSec;

            CpuText = s.CpuPercent is double c ? $"{c:0}%" : "—";
            RamText = $"{s.RamLoadPercent:0}%";
            RamSubText = $"{ByteFormatter.Format((long)s.RamUsedBytes)} / {ByteFormatter.Format((long)s.RamTotalBytes)}";
            NetText = $"↓ {ByteFormatter.Format((long)s.NetRecvBytesPerSec)}/s   ↑ {ByteFormatter.Format((long)s.NetSentBytesPerSec)}/s";

            Push(_cpu, cpu);
            Push(_ram, s.RamLoadPercent);
            Push(_net, netTotal);

            CpuHistory = _cpu.ToArray();
            RamHistory = _ram.ToArray();
            NetHistory = _net.ToArray();
            NetMax = Math.Max(1, _net.Count == 0 ? 1 : _net.Max());

            if (wantDrives) UpdateDrives(s.Drives);
            if (_allRows.Count > 0 && _tempTick++ % 2 == 0) _ = RefreshTemperaturesAsync();
        }
        catch (Exception)
        {
            // a transient snapshot failure should not take the page down
        }
        finally
        {
            _readingSnapshot = false;
        }
    }

    private void UpdateDrives(IReadOnlyList<DriveStat> drives)
    {
        if (_storage is null) return;

        foreach (var d in drives)
        {
            var existing = _storage.Drives.FirstOrDefault(v => v.Name == d.Name);
            if (existing is null)
            {
                existing = new DriveViewModel(d.Name, d.Label);
                _storage.Drives.Add(existing);
            }
            existing.UsedPercent = d.UsedPercent;
            existing.CapacityText = $"{ByteFormatter.Format(d.FreeBytes)} free of {ByteFormatter.Format(d.TotalBytes)}";
        }

        // Drop volumes that disappeared (ejected/unmounted).
        for (int i = _storage.Drives.Count - 1; i >= 0; i--)
            if (drives.All(d => d.Name != _storage.Drives[i].Name))
                _storage.Drives.RemoveAt(i);

        _storage.HasDrives = _storage.Drives.Count > 0;
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
            foreach (var row in _allRows)
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
