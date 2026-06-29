using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

/// <summary>One live sensor row (its value + severity colour update in place each tick).</summary>
public partial class SensorRowViewModel(string name, SensorKind kind) : ObservableObject
{
    private static readonly Brush NormalBrush = Frozen(Color.FromRgb(0xE6, 0xF7, 0xFF));
    private static readonly Brush WarmBrush = Frozen(Color.FromRgb(0xFF, 0xD3, 0x00));
    private static readonly Brush HotBrush = Frozen(Color.FromRgb(0xFF, 0x2A, 0x6D));

    public string Name { get; } = name;

    [ObservableProperty] private string _valueText = "—";
    [ObservableProperty] private Brush _valueBrush = NormalBrush;

    public void Update(double value)
    {
        ValueText = SensorFormatting.Format(kind, value);
        ValueBrush = kind == SensorKind.Temperature
            ? SensorFormatting.Severity(value) switch
            {
                TempSeverity.Hot => HotBrush,
                TempSeverity.Warm => WarmBrush,
                _ => NormalBrush,
            }
            : NormalBrush;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

public class SensorGroupViewModel(string header)
{
    public string Header { get; } = header;
    public ObservableCollection<SensorRowViewModel> Rows { get; } = [];
}

public class SensorComponentViewModel(string name, SymbolRegular icon)
{
    public string Name { get; } = name;
    public SymbolRegular Icon { get; } = icon;
    public ObservableCollection<SensorGroupViewModel> Groups { get; } = [];
}

public partial class SensorsViewModel : ObservableObject
{
    private readonly ISensorMonitorService _sensors;
    private readonly Dictionary<string, SensorRowViewModel> _rowIndex = new();
    private bool _built;

    public ObservableCollection<SensorComponentViewModel> Components { get; } = [];

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusText = "Reading hardware sensors…";

    // Headline graphs.
    [ObservableProperty] private bool _hasGpu;
    [ObservableProperty] private string _cpuTempText = "—";
    [ObservableProperty] private string _cpuLoadText = "—";
    [ObservableProperty] private string _gpuTempText = "—";
    [ObservableProperty] private string _gpuLoadText = "—";
    [ObservableProperty] private IReadOnlyList<double>? _cpuTempHistory;
    [ObservableProperty] private IReadOnlyList<double>? _cpuLoadHistory;
    [ObservableProperty] private IReadOnlyList<double>? _gpuTempHistory;
    [ObservableProperty] private IReadOnlyList<double>? _gpuLoadHistory;

    public SensorsViewModel(ISensorMonitorService sensors) => _sensors = sensors;

    public void OnNavigatedTo()
    {
        _sensors.Updated += OnUpdated;
        _sensors.AddConsumer();
        Render(_sensors.Current); // show last-known values instantly
    }

    public void OnNavigatedFrom()
    {
        _sensors.Updated -= OnUpdated;
        _sensors.RemoveConsumer();
    }

    private void OnUpdated(object? sender, EventArgs e) => Render(_sensors.Current);

    private void Render(IReadOnlyList<SensorReading> readings)
    {
        if (readings.Count == 0)
        {
            HasData = false;
            StatusText = "Sensors unavailable — your system may block the sensor driver " +
                         "(Memory Integrity / HVCI), or no compatible sensors were found.";
            return;
        }

        HasData = true;
        int components = readings.Select(r => r.Component).Distinct().Count();
        StatusText = $"{readings.Count} sensors across {components} component{(components == 1 ? "" : "s")}";

        if (!_built || readings.Any(r => !_rowIndex.ContainsKey(SensorMonitorService.Key(r))))
            BuildTree(readings);

        foreach (var r in readings)
            if (_rowIndex.TryGetValue(SensorMonitorService.Key(r), out var row))
                row.Update(r.Value);

        UpdateHeadline(readings);
    }

    private void BuildTree(IReadOnlyList<SensorReading> readings)
    {
        Components.Clear();
        _rowIndex.Clear();
        foreach (var compGroup in readings.GroupBy(r => r.Component))
        {
            var comp = new SensorComponentViewModel(compGroup.Key, IconFor(compGroup.First().Category));
            foreach (var kindGroup in compGroup.GroupBy(r => r.Kind).OrderBy(g => (int)g.Key))
            {
                var group = new SensorGroupViewModel(SensorFormatting.KindLabel(kindGroup.Key));
                foreach (var r in kindGroup.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var row = new SensorRowViewModel(r.Name, r.Kind);
                    group.Rows.Add(row);
                    _rowIndex[SensorMonitorService.Key(r)] = row;
                }
                comp.Groups.Add(group);
            }
            Components.Add(comp);
        }
        _built = true;
    }

    private void UpdateHeadline(IReadOnlyList<SensorReading> readings)
    {
        var cpuTemp = Pick(readings, "Processor", SensorKind.Temperature);
        var cpuLoad = Pick(readings, "Processor", SensorKind.Load, prefer: "total");
        var gpuTemp = Pick(readings, "Graphics", SensorKind.Temperature);
        var gpuLoad = Pick(readings, "Graphics", SensorKind.Load, prefer: "core");

        SetHeadline(cpuTemp, v => CpuTempText = v, h => CpuTempHistory = h);
        SetHeadline(cpuLoad, v => CpuLoadText = v, h => CpuLoadHistory = h);
        HasGpu = gpuTemp is not null || gpuLoad is not null;
        SetHeadline(gpuTemp, v => GpuTempText = v, h => GpuTempHistory = h);
        SetHeadline(gpuLoad, v => GpuLoadText = v, h => GpuLoadHistory = h);
    }

    private void SetHeadline(SensorReading? r, Action<string> setText, Action<IReadOnlyList<double>?> setHistory)
    {
        if (r is null) { setText("—"); setHistory(null); return; }
        setText(SensorFormatting.Format(r.Kind, r.Value));
        setHistory(_sensors.History(SensorMonitorService.Key(r)));
    }

    // Highest-value sensor of a category+kind (the representative reading), preferring a name match.
    private static SensorReading? Pick(IReadOnlyList<SensorReading> readings, string category, SensorKind kind, string? prefer = null)
    {
        var candidates = readings.Where(r => r.Category == category && r.Kind == kind).ToList();
        if (candidates.Count == 0) return null;
        if (prefer is not null)
        {
            var named = candidates.FirstOrDefault(r => r.Name.Contains(prefer, StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named;
        }
        return candidates.OrderByDescending(r => r.Value).First();
    }

    private static SymbolRegular IconFor(string category) => category switch
    {
        "Disk" => SymbolRegular.HardDrive24,
        "Motherboard" => SymbolRegular.Options24,
        _ => SymbolRegular.DeveloperBoard24,
    };
}
