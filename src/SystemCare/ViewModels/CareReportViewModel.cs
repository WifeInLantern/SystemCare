using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

/// <summary>One category row in the "actions by category" breakdown.</summary>
public class CategoryRowViewModel(string name, int count, long bytes, int maxCount)
{
    public string Name => name;
    public string CountText => $"{count:N0}×";
    public string BytesText => bytes > 0 ? ByteFormatter.Format(bytes) : "";
    public double PercentOfMax => maxCount > 0 ? count * 100.0 / maxCount : 0;
}

/// <summary>
/// The Care Report page: charts and stats aggregated from the activity history, health-score
/// snapshots, and benchmark history — plus a one-click self-contained HTML export.
/// </summary>
public partial class CareReportViewModel : ObservableObject
{
    private const int DailyDays = 30;
    private const int WeeklyWeeks = 12;

    private readonly IHistoryService _history;
    private readonly IHealthTrendService _healthTrend;
    private readonly IBenchmarkHistoryService _benchHistory;
    private readonly ISettingsService _settings;
    private readonly ICareReportExporter _exporter;
    private readonly ISnackbarService _snackbar;
    private readonly ILogService _log;

    public ObservableCollection<CategoryRowViewModel> CategoryRows { get; } = [];

    // Headline tiles
    [ObservableProperty] private string _totalFreedText = "—";
    [ObservableProperty] private long _totalFreedBytes;
    [ObservableProperty] private string _totalActionsText = "—";
    [ObservableProperty] private double _totalActionsValue;
    [ObservableProperty] private string _healthScoreText = "—";
    [ObservableProperty] private double _healthScoreValue = double.NaN;
    [ObservableProperty] private string _latestBenchmarkText = "—";
    [ObservableProperty] private double _latestBenchmarkValue = double.NaN;
    [ObservableProperty] private string _historyWindowText = "";

    // Space-freed bar charts
    [ObservableProperty] private IReadOnlyList<double>? _dailyBars;
    [ObservableProperty] private double _dailyMax = 1;
    [ObservableProperty] private string _dailyRangeText = "";
    [ObservableProperty] private IReadOnlyList<double>? _weeklyBars;
    [ObservableProperty] private double _weeklyMax = 1;
    [ObservableProperty] private string _weeklyRangeText = "";
    [ObservableProperty] private bool _hasActivity;

    // Trends
    [ObservableProperty] private IReadOnlyList<double>? _healthTrendValues;
    [ObservableProperty] private bool _hasHealthTrend;
    [ObservableProperty] private IReadOnlyList<double>? _benchmarkTrendValues;
    [ObservableProperty] private double _benchmarkTrendMax = 100;
    [ObservableProperty] private bool _hasBenchmarkTrend;

    [ObservableProperty] private bool _isExporting;

    public CareReportViewModel(IHistoryService history, IHealthTrendService healthTrend,
        IBenchmarkHistoryService benchHistory, ISettingsService settings,
        ICareReportExporter exporter, ISnackbarService snackbar, ILogService log)
    {
        _history = history;
        _healthTrend = healthTrend;
        _benchHistory = benchHistory;
        _settings = settings;
        _exporter = exporter;
        _snackbar = snackbar;
        _log = log;
    }

    public void OnNavigatedTo() => Load();

    private void Load()
    {
        var entries = _history.GetAll();
        var totals = CareReportAggregator.Totals(entries);

        TotalFreedText = ByteFormatter.Format(totals.TotalBytes);
        TotalFreedBytes = totals.TotalBytes;
        TotalActionsText = totals.TotalActions.ToString("N0");
        TotalActionsValue = totals.TotalActions;
        HealthScoreText = _settings.Current.LastHealthScore is int score ? $"{score}" : "—";
        HealthScoreValue = _settings.Current.LastHealthScore is int hs ? hs : double.NaN;
        HistoryWindowText = totals.OldestUtc is DateTime oldest
            ? $"Based on the last {totals.TotalActions:N0} recorded action(s), since {oldest.ToLocalTime():d} (history keeps the most recent 500)."
            : "Run scans and cleanups to build up trend data.";
        HasActivity = totals.TotalActions > 0;

        var daily = CareReportAggregator.DailyBytesFreed(entries, DailyDays);
        DailyBars = daily.Select(d => (double)d.Bytes).ToList();
        DailyMax = Math.Max(1, daily.Max(d => d.Bytes));
        DailyRangeText = $"{daily[0].Day:d} – {daily[^1].Day:d} · biggest day {ByteFormatter.Format(daily.Max(d => d.Bytes))}";

        var weekly = CareReportAggregator.WeeklyBytesFreed(entries, WeeklyWeeks);
        WeeklyBars = weekly.Select(w => (double)w.Bytes).ToList();
        WeeklyMax = Math.Max(1, weekly.Max(w => w.Bytes));
        WeeklyRangeText = $"{weekly[0].WeekStart:d} – today · biggest week {ByteFormatter.Format(weekly.Max(w => w.Bytes))}";

        CategoryRows.Clear();
        var breakdown = CareReportAggregator.CategoryBreakdown(entries);
        int maxCount = breakdown.Count > 0 ? breakdown[0].Count : 0;
        foreach (var row in breakdown)
            CategoryRows.Add(new CategoryRowViewModel(row.Category, row.Count, row.Bytes, maxCount));

        var health = _healthTrend.GetAll();
        HasHealthTrend = health.Count >= 2;
        HealthTrendValues = HasHealthTrend ? health.Select(s => (double)s.Score).ToList() : null;

        var runs = _benchHistory.GetAll();
        HasBenchmarkTrend = runs.Count >= 2;
        if (HasBenchmarkTrend)
        {
            var points = runs.Select(r => (double)r.Points).ToList();
            BenchmarkTrendValues = points;
            BenchmarkTrendMax = Math.Max(100, points.Max() * 1.1);
            LatestBenchmarkText = $"{runs[^1].Points:N0} pts";
            LatestBenchmarkValue = runs[^1].Points;
        }
        else
        {
            BenchmarkTrendValues = null;
            LatestBenchmarkText = runs.Count == 1 ? $"{runs[^1].Points:N0} pts" : "—";
            LatestBenchmarkValue = runs.Count == 1 ? runs[^1].Points : double.NaN;
        }
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"SystemCare Report {DateTime.Now:yyyy-MM-dd}.html",
            Filter = "HTML report|*.html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog() != true) return;

        IsExporting = true;
        try
        {
            string path = await _exporter.ExportAsync(dialog.FileName);
            _snackbar.Show("Report exported", $"Saved to {path}", ControlAppearance.Success, null, TimeSpan.FromSeconds(6));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn("CareReport", $"Export failed: {ex.Message}");
            _snackbar.Show("Export failed", ex.Message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(6));
        }
        finally
        {
            IsExporting = false;
        }
    }
}
