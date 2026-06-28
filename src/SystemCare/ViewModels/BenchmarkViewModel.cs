using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class BenchmarkViewModel : ObservableObject
{
    private readonly IBenchmarkService _benchmark;
    private readonly IBenchmarkHistoryService _benchHistory;
    private readonly IHistoryService _history;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty] private double _overallScore = -1; // -1 => the gauge shows "not scored yet"
    [ObservableProperty] private int _points;
    [ObservableProperty] private string _tier = ""; // performance band shown under the gauge score
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _currentPhase =
        "Run a benchmark to score your PC's CPU, memory, and disk.";

    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private double _cpuScore;
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private double _ramScore;
    [ObservableProperty] private string _diskText = "—";
    [ObservableProperty] private double _diskScore;

    [ObservableProperty] private IReadOnlyList<double>? _trend;
    [ObservableProperty] private double _trendMax = 100;
    [ObservableProperty] private bool _hasTrend;

    public BenchmarkViewModel(IBenchmarkService benchmark, IBenchmarkHistoryService benchHistory, IHistoryService history)
    {
        _benchmark = benchmark;
        _benchHistory = benchHistory;
        _history = history;
    }

    public void OnNavigatedTo() => LoadTrend();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        HasResult = false;
        OverallScore = -1;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<BenchmarkProgress>(p =>
        {
            CurrentPhase = p.Phase;
            Progress = p.Percent;
        });

        try
        {
            var r = await _benchmark.RunAsync(progress, _cts.Token);

            CpuText = r.CpuText; CpuScore = r.CpuScore;
            RamText = r.RamText; RamScore = r.RamScore;
            DiskText = r.DiskText; DiskScore = r.DiskScore;
            Points = r.Points;
            Tier = TierFor(r.OverallIndex);
            OverallScore = r.OverallIndex;
            HasResult = true;
            CurrentPhase = $"Score: {r.Points:N0} points · CPU {r.CpuScore:0} · RAM {r.RamScore:0} · Disk {r.DiskScore:0}";

            _benchHistory.Add(new BenchmarkRun
            {
                TimestampUtc = DateTime.UtcNow,
                CpuMOps = r.CpuMOps,
                RamGBps = r.RamGBps,
                DiskMBps = r.DiskMBps,
                CpuScore = r.CpuScore,
                RamScore = r.RamScore,
                DiskScore = r.DiskScore,
                OverallIndex = r.OverallIndex,
                Points = r.Points,
            });
            _history.Record("Benchmark",
                $"Scored {r.Points:N0} points (CPU {r.CpuScore:0}, RAM {r.RamScore:0}, Disk {r.DiskScore:0})",
                icon: "Gauge24");
            LoadTrend();
        }
        catch (OperationCanceledException)
        {
            CurrentPhase = "Benchmark cancelled.";
        }
        catch (Exception ex)
        {
            CurrentPhase = $"Benchmark failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanRun() => !IsRunning;

    // Performance tiers aligned with the gauge's colour bands (mint / cyan / yellow / magenta).
    private static string TierFor(double index) => index switch
    {
        >= 90 => "Elite",
        >= 70 => "Fast",
        >= 40 => "Mainstream",
        _ => "Entry",
    };

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void LoadTrend()
    {
        var runs = _benchHistory.GetAll();
        if (runs.Count >= 2)
        {
            var points = runs.Select(r => (double)r.Points).ToList();
            Trend = points;
            TrendMax = Math.Max(100, points.Max() * 1.1);
            HasTrend = true;
        }
        else
        {
            HasTrend = false;
        }
    }
}
