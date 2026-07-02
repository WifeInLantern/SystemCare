using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

/// <summary>One recommendation card. Direct fixes flip to Done in place; review actions navigate.</summary>
public partial class RecommendationItemViewModel(Recommendation rec) : ObservableObject
{
    public Recommendation Rec { get; } = rec;
    public string Title => Rec.Title;
    public string Explanation => Rec.Explanation;
    public string ImpactText => Rec.ImpactText;
    public string Icon => Rec.Icon;
    public string ActionLabel => Rec.IsDirectFix ? "Apply" : "Review";
    public string SeverityText => Rec.Severity switch
    {
        RecommendationSeverity.Important => "IMPORTANT",
        RecommendationSeverity.Suggested => "SUGGESTED",
        _ => "INFO",
    };
    public Brush SeverityBrush => Rec.Severity switch
    {
        RecommendationSeverity.Important => ImportantBrush,
        RecommendationSeverity.Suggested => SuggestedBrush,
        _ => InfoBrush,
    };

    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isBusy;

    private static readonly Brush ImportantBrush = Frozen(0xFF, 0x2A, 0x6D); // magenta
    private static readonly Brush SuggestedBrush = Frozen(0xFF, 0xD3, 0x00); // yellow
    private static readonly Brush InfoBrush = Frozen(0x00, 0xE5, 0xFF);      // cyan

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// The Auto Care page: one click runs read-only probes and lists ranked, explained recommendations.
/// Direct fixes (junk clean, RAM trim) apply in place via the same services and safety gates the
/// Dashboard uses; review items deep-link to the relevant tool.
/// </summary>
public partial class AutoCareViewModel : ObservableObject
{
    private readonly IAutoCareService _autoCare;
    private readonly IJunkScanService _junkScan;
    private readonly IMemoryOptimizerService _memoryOptimizer;
    private readonly ISettingsService _settings;
    private readonly IHealthTrendService _healthTrend;
    private readonly IBackupConfirmationService _backup;
    private readonly IRestorePointService _restore;
    private readonly ISnackbarService _snackbar;
    private readonly IHistoryService _history;
    private readonly ILogService _log;

    private AutoCareAnalysis? _lastAnalysis;

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = [];

    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private bool _hasAnalyzed;
    [ObservableProperty] private bool _allClear;
    [ObservableProperty] private double _healthScoreValue = -1;
    [ObservableProperty] private string _headline = "Analyze this PC to get ranked, explained recommendations.";
    [ObservableProperty] private string _progressText = "";

    public AutoCareViewModel(IAutoCareService autoCare, IJunkScanService junkScan,
        IMemoryOptimizerService memoryOptimizer, ISettingsService settings, IHealthTrendService healthTrend,
        IBackupConfirmationService backup, IRestorePointService restore, ISnackbarService snackbar,
        IHistoryService history, ILogService log)
    {
        _autoCare = autoCare;
        _junkScan = junkScan;
        _memoryOptimizer = memoryOptimizer;
        _settings = settings;
        _healthTrend = healthTrend;
        _backup = backup;
        _restore = restore;
        _snackbar = snackbar;
        _history = history;
        _log = log;

        if (_settings.Current.LastHealthScore is int saved) HealthScoreValue = saved;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task AnalyzeAsync(CancellationToken ct)
    {
        IsWorking = true;
        AllClear = false;
        try
        {
            var progress = new Progress<string>(p => ProgressText = p);
            var analysis = await _autoCare.AnalyzeAsync(progress, ct);
            _lastAnalysis = analysis;

            Recommendations.Clear();
            foreach (var rec in analysis.Recommendations)
                Recommendations.Add(new RecommendationItemViewModel(rec));

            int score = analysis.Probes.Health.Score;
            HealthScoreValue = score;
            HasAnalyzed = true;
            AllClear = Recommendations.Count == 0;

            double recoverable = RecommendationBuilder.PointsRecoverable(analysis.Recommendations, score);
            Headline = AllClear
                ? $"Health {score}/100 — nothing to fix. This PC is in great shape."
                : $"Health {score}/100 — {Recommendations.Count} recommendation(s)" +
                  (recoverable >= 1 ? $", up to +{recoverable:0} points recoverable." : ".");
            ProgressText = "";

            // Keep the dashboard gauge and the Care Report trend in sync with this scan.
            _settings.Current.LastScanUtc = DateTime.UtcNow;
            _settings.Current.LastHealthScore = score;
            _settings.Save();
            _healthTrend.Record(score);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Analysis cancelled.";
        }
        catch (Exception ex)
        {
            _log.Warn("AutoCare", $"Analysis failed: {ex.Message}");
            ProgressText = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync(RecommendationItemViewModel? item)
    {
        if (item is null || item.IsDone || item.IsBusy) return;

        switch (item.Rec.Action)
        {
            case RecommendationAction.CleanJunk:
                await ApplyJunkCleanAsync(item);
                break;
            case RecommendationAction.TrimRam:
                await ApplyRamTrimAsync(item);
                break;
            default:
                if (item.Rec.NavigateTarget is string target &&
                    System.Windows.Application.Current.MainWindow is MainWindow window)
                    window.NavigateTo(target);
                break;
        }
    }

    private async Task ApplyJunkCleanAsync(RecommendationItemViewModel item)
    {
        if (_lastAnalysis?.Probes.Junk is not JunkScanResult scan) return;
        item.IsBusy = true;
        try
        {
            if (await _backup.ConfirmRestorePointAsync("the Auto Care cleanup"))
            {
                ProgressText = "Creating a restore point…";
                await _restore.CreateRestorePointAsync("Before SystemCare Auto Care");
            }

            ProgressText = "Cleaning junk files…";
            var result = await _junkScan.CleanAsync(scan, _lastAnalysis.JunkCategoryIds, null, CancellationToken.None);

            _history.Record("Auto care",
                $"Cleaned {ByteFormatter.Format(result.BytesRemoved)} of junk",
                result.BytesRemoved, result.FilesRemoved, "Sparkle24");
            _snackbar.Show("Junk cleaned",
                $"Removed {ByteFormatter.Format(result.BytesRemoved)} across {result.FilesRemoved:N0} files. Re-analyze to refresh your score.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(6));
            item.IsDone = true;
            ProgressText = "";
        }
        catch (Exception ex)
        {
            _log.Warn("AutoCare", $"Junk clean failed: {ex.Message}");
            _snackbar.Show("Cleanup failed", ex.Message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(6));
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task ApplyRamTrimAsync(RecommendationItemViewModel item)
    {
        item.IsBusy = true;
        try
        {
            var result = await _memoryOptimizer.OptimizeAsync();
            _history.Record("Auto care",
                $"Freed {ByteFormatter.Format(result.BytesFreed)} of RAM",
                result.BytesFreed, result.ProcessesTrimmed, "Sparkle24");
            _snackbar.Show("Memory freed",
                $"Freed {ByteFormatter.Format(result.BytesFreed)} across {result.ProcessesTrimmed} processes.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
            item.IsDone = true;
        }
        catch (Exception ex)
        {
            _log.Warn("AutoCare", $"RAM trim failed: {ex.Message}");
            _snackbar.Show("Trim failed", ex.Message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(6));
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
