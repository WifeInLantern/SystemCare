using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

/// <summary>Shared icon/label/colour mapping for reliability categories + relative-time formatting.</summary>
internal static class ReliabilityVisuals
{
    private static readonly Brush Warn = Frozen(0xFF, 0xD3, 0x00);
    private static readonly Brush Err = Frozen(0xFF, 0x8A, 0x3D);
    private static readonly Brush Crit = Frozen(0xFF, 0x2A, 0x6D);

    public static SymbolRegular Icon(ReliabilityCategory c) => c switch
    {
        ReliabilityCategory.BlueScreen => SymbolRegular.ErrorCircle24,
        ReliabilityCategory.UnexpectedShutdown => SymbolRegular.Power24,
        ReliabilityCategory.Crash => SymbolRegular.Bug24,
        ReliabilityCategory.AppHang => SymbolRegular.Hourglass24,
        ReliabilityCategory.DiskError => SymbolRegular.HardDrive24,
        ReliabilityCategory.ServiceFailure => SymbolRegular.Settings24,
        _ => SymbolRegular.Warning24,
    };

    public static string Label(ReliabilityCategory c) => c switch
    {
        ReliabilityCategory.BlueScreen => "Blue screens",
        ReliabilityCategory.UnexpectedShutdown => "Shutdowns",
        ReliabilityCategory.Crash => "App crashes",
        ReliabilityCategory.AppHang => "Hangs",
        ReliabilityCategory.DiskError => "Disk errors",
        ReliabilityCategory.ServiceFailure => "Service failures",
        _ => c.ToString(),
    };

    public static Brush Severity(ReliabilitySeverity s) => s switch
    {
        ReliabilitySeverity.Critical => Crit,
        ReliabilitySeverity.Error => Err,
        _ => Warn,
    };

    public static string RelativeTime(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public class ReliabilityEventViewModel(ReliabilityEvent e)
{
    public SymbolRegular Icon { get; } = ReliabilityVisuals.Icon(e.Category);
    public Brush SeverityBrush { get; } = ReliabilityVisuals.Severity(e.Severity);
    public string Title { get; } = e.Title;
    public string Source { get; } = e.Source;
    public string TimeText { get; } = ReliabilityVisuals.RelativeTime(e.TimeUtc);
}

public class ReliabilityCountViewModel(string label, int count, SymbolRegular icon)
{
    public string Label { get; } = label;
    public int Count { get; } = count;
    public SymbolRegular Icon { get; } = icon;
}

public partial class ReliabilityViewModel : ObservableObject
{
    private const int Days = 14;

    private readonly IReliabilityService _reliability;
    private readonly IRestorePointService _restore;
    private readonly ISnackbarService _snackbar;
    private bool _loaded;

    public ObservableCollection<ReliabilityCountViewModel> Counts { get; } = [];
    public ObservableCollection<ReliabilityEventViewModel> Events { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _score = -1; // -1 => gauge shows "not scored yet"
    [ObservableProperty] private string _tier = "";
    [ObservableProperty] private bool _hasIssues;
    [ObservableProperty] private bool _readOk = true;
    [ObservableProperty] private string _summaryText = "Scanning the event log for recent problems…";

    public ReliabilityViewModel(IReliabilityService reliability, IRestorePointService restore, ISnackbarService snackbar)
    {
        _reliability = reliability;
        _restore = restore;
        _snackbar = snackbar;
    }

    public async void OnNavigatedTo()
    {
        if (_loaded) return;
        await LoadAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var report = await _reliability.GetAsync(Days);
            _loaded = true;
            ReadOk = report.Read;
            Score = report.Score;
            Tier = ReliabilityScore.Tier(report.Score);

            Events.Clear();
            foreach (var e in report.Events) Events.Add(new ReliabilityEventViewModel(e));
            HasIssues = report.Events.Count > 0;
            BuildCounts(report.Events);

            SummaryText = !report.Read
                ? "Couldn't read the Windows Event Log on this system."
                : report.Events.Count == 0
                    ? $"No reliability problems found in the last {Days} days — nice and stable."
                    : $"{report.Events.Count} issue{(report.Events.Count == 1 ? "" : "s")} found in the last {Days} days.";
        }
        catch (Exception ex)
        {
            SummaryText = $"Reliability scan failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildCounts(IReadOnlyList<ReliabilityEvent> events)
    {
        Counts.Clear();
        foreach (var cat in new[]
        {
            ReliabilityCategory.BlueScreen, ReliabilityCategory.UnexpectedShutdown, ReliabilityCategory.Crash,
            ReliabilityCategory.AppHang, ReliabilityCategory.DiskError, ReliabilityCategory.ServiceFailure,
        })
        {
            int n = events.Count(e => e.Category == cat);
            Counts.Add(new ReliabilityCountViewModel(ReliabilityVisuals.Label(cat), n, ReliabilityVisuals.Icon(cat)));
        }
    }

    [RelayCommand]
    private void OpenSystemRepair() => Navigate("DiskHealth");

    [RelayCommand]
    private void OpenRescueCenter() => Navigate("RescueCenter");

    [RelayCommand]
    private void OpenEventViewer()
    {
        try { Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        var (ok, message) = await _restore.CreateRestorePointAsync($"SystemCare — {DateTime.Now:g}");
        _snackbar.Show(ok ? "Restore point created" : "Restore point", message,
            ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
    }

    private static void Navigate(string page)
    {
        if (Application.Current.MainWindow is SystemCare.MainWindow window)
            window.NavigateTo(page);
    }
}
