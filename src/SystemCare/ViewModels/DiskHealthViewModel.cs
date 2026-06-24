using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class DiskVolumeViewModel(string letter, string label, long total, long free, DiskHealthViewModel owner)
{
    public string Letter { get; } = letter;          // e.g. "C:"
    public string Label { get; } = label;
    public long TotalBytes { get; } = total;
    public long FreeBytes { get; } = free;
    public double UsedPercent => TotalBytes > 0 ? (TotalBytes - FreeBytes) * 100.0 / TotalBytes : 0;
    public string FreeText => $"{ByteFormatter.Format(FreeBytes)} free of {ByteFormatter.Format(TotalBytes)}";

    [RelayCommand] private Task CheckErrors() => owner.CheckErrorsAsync(this);
    [RelayCommand] private Task Optimize() => owner.OptimizeAsync(this);
}

/// <summary>Display card for one physical drive: score ring + SMART chips.</summary>
public class DiskCardViewModel(PhysicalDiskHealth disk)
{
    public PhysicalDiskHealth Disk { get; } = disk;
    public string Name => Disk.Name;
    public string Media => Disk.MediaType;
    public string MediaIcon => Disk.MediaType is "SSD" or "SCM" ? "Flash24" : "HardDrive24";
    public string SizeText => ByteFormatter.Format(Disk.SizeBytes);
    public double Score => Disk.Score;               // -1 = not scored (HealthGauge shows "not scanned")
    public string Band => Disk.ScoreBand;
    public string HealthText => Disk.HealthText;

    public bool HasTemp => Disk.TemperatureC is > 0;
    public string TempText => Disk.TemperatureC is double t ? $"{t:0}°C" : "";
    public bool HasWear => Disk.WearPercent is int w && (w > 0 || Disk.MediaType is "SSD" or "SCM");
    public string WearText => Disk.WearPercent is int w ? $"{w}% wear" : "";
    public bool HasHours => Disk.PowerOnHours is > 0;
    public string HoursText => Disk.PowerOnHours is long h ? $"{h:n0} h powered on" : "";
    public bool HasErrors => Disk.ReallocatedSectors is > 0;
    public string ErrorsText => Disk.ReallocatedSectors is long s ? $"{s} bad sector(s)" : "";
}

public class DiskAlertViewModel(DiskAlert alert)
{
    public DiskAlert Alert { get; } = alert;
    public int UrgencyRank => (int)Alert.Urgency;
    public DiskUrgency Urgency => Alert.Urgency;
    public string Title => Alert.Title;
    public string Detail => Alert.Detail;
    public string ActionLabel => Alert.ActionLabel;
    public bool HasAction => !string.IsNullOrWhiteSpace(Alert.ActionLabel);
    public string? ActionTarget => Alert.ActionTarget;

    public string Icon => Alert.Urgency switch
    {
        DiskUrgency.Critical => "ErrorCircle24",
        DiskUrgency.Warning => "Warning24",
        _ => "Info24",
    };
    public string Brush => Alert.Urgency switch
    {
        DiskUrgency.Critical => "#FF2A6D",
        DiskUrgency.Warning => "#FF8A3D",
        DiskUrgency.Caution => "#FFD300",
        _ => "#00E5FF",
    };
}

public partial class DiskHealthViewModel : ObservableObject
{
    private const int MaxOutputChars = 60_000;
    private readonly IDiskMaintenanceService _service;
    private readonly IDiskHealthScoreService _score;
    private readonly IScheduledMaintenanceService _maintenance;
    private readonly IRestorePointService _restore;
    private readonly IBackupConfirmationService _backup;
    private readonly ISnackbarService _snackbar;
    private readonly ITrayIconService _tray;
    private readonly IHistoryService _history;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<DiskCardViewModel> DiskCards { get; } = [];
    public ObservableCollection<DiskVolumeViewModel> Volumes { get; } = [];
    public ObservableCollection<DiskAlertViewModel> Alerts { get; } = [];

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSfcCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDismCommand))]
    [NotifyCanExecuteChangedFor(nameof(MaintainAllCommand))]
    private bool _isRunning;
    [ObservableProperty] private string _currentOperation = "";
    [ObservableProperty] private string _outputText = "Run a check or maintenance task to see live output here.";
    [ObservableProperty] private double _overallScore = -1;
    [ObservableProperty] private string _summaryText = "Reading drive health…";
    [ObservableProperty] private bool _hasAlerts;

    public DiskHealthViewModel(IDiskMaintenanceService service, IDiskHealthScoreService score,
        IScheduledMaintenanceService maintenance, IRestorePointService restore,
        ISnackbarService snackbar, ITrayIconService tray, IHistoryService history,
        IBackupConfirmationService backup)
    {
        _service = service;
        _score = score;
        _maintenance = maintenance;
        _restore = restore;
        _backup = backup;
        _snackbar = snackbar;
        _tray = tray;
        _history = history;
    }

    public async void OnNavigatedTo()
    {
        if (DiskCards.Count > 0 || Volumes.Count > 0) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            Volumes.Clear();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    Volumes.Add(new DiskVolumeViewModel(
                        drive.Name.TrimEnd('\\'),
                        string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                        drive.TotalSize, drive.TotalFreeSpace, this));
                }
                catch (Exception) { }
            }

            var disks = await _service.GetPhysicalDisksAsync();
            foreach (var disk in disks)
            {
                disk.Score = _score.Score(disk);
                disk.ScoreBand = _score.Band(disk.Score);
            }

            DiskCards.Clear();
            foreach (var disk in disks) DiskCards.Add(new DiskCardViewModel(disk));

            BuildAlerts(disks);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildAlerts(List<PhysicalDiskHealth> disks)
    {
        var all = new List<DiskAlertViewModel>();
        foreach (var disk in disks)
            foreach (var a in _score.Alerts(disk))
                all.Add(new DiskAlertViewModel(a));

        // Volume-level: low free space (storage efficiency tie-in).
        foreach (var v in Volumes)
            if (v.TotalBytes > 0 && v.FreeBytes * 100.0 / v.TotalBytes < 10)
                all.Add(new DiskAlertViewModel(new DiskAlert
                {
                    Urgency = DiskUrgency.Caution,
                    Title = $"{v.Letter} is almost full",
                    Detail = $"Only {v.FreeText}. Free up space to keep the drive healthy and fast.",
                    ActionLabel = "Free up space", ActionTarget = "Cleanup",
                }));

        Alerts.Clear();
        foreach (var a in all.OrderByDescending(a => a.UrgencyRank)) Alerts.Add(a);
        HasAlerts = Alerts.Count > 0;

        OverallScore = DiskCards.Count == 0 ? -1 : disks.Min(d => d.Score);
        SummaryText = BuildSummary(disks);

        // Surface the worst alert proactively.
        var worst = all.OrderByDescending(a => a.UrgencyRank).FirstOrDefault();
        if (worst is { Urgency: DiskUrgency.Critical })
        {
            _snackbar.Show(worst.Title, worst.Detail, ControlAppearance.Danger, null, TimeSpan.FromSeconds(8));
            _tray.ShowBalloon(worst.Title, worst.Detail);
        }
        else if (worst is { Urgency: DiskUrgency.Warning })
        {
            _snackbar.Show(worst.Title, worst.Detail, ControlAppearance.Caution, null, TimeSpan.FromSeconds(6));
        }
    }

    private string BuildSummary(List<PhysicalDiskHealth> disks)
    {
        if (disks.Count == 0) return "No physical drives detected.";
        int worst = disks.Min(d => d.Score);
        string band = _score.Band(worst).ToLowerInvariant();
        return Alerts.Count == 0
            ? $"{disks.Count} drive(s) · all healthy — no action needed."
            : $"{disks.Count} drive(s) · {band} — {Alerts.Count} item(s) need attention.";
    }

    // ---- deep-links + alert actions ----

    [RelayCommand]
    private void Navigate(string page)
    {
        if (Application.Current.MainWindow is MainWindow window)
            window.NavigateTo(page);
    }

    [RelayCommand]
    private async Task AlertAction(DiskAlertViewModel? alert)
    {
        switch (alert?.ActionTarget)
        {
            case null or "":
                return;
            case "__maintain":
                await MaintainAll();
                break;
            case "__restorepoint":
                var (ok, message) = await _restore.CreateRestorePointAsync("Disk Health — backup point");
                _snackbar.Show(ok ? "Restore point created" : "Restore point",
                    message, ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
                break;
            default:
                Navigate(alert.ActionTarget);
                break;
        }
    }

    [RelayCommand]
    private async Task CreateRestorePoint()
    {
        var (ok, message) = await _restore.CreateRestorePointAsync("Disk Health — backup point");
        _snackbar.Show(ok ? "Restore point created" : "Restore point",
            message, ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
    }

    // ---- per-volume + global maintenance ----

    public Task CheckErrorsAsync(DiskVolumeViewModel volume) =>
        // Read-only check (no /f): reports filesystem errors without locking the volume. No restore point needed.
        RunOperationAsync($"Checking {volume.Letter} for errors", "chkdsk", volume.Letter, null, restorePoint: false);

    public Task OptimizeAsync(DiskVolumeViewModel volume) =>
        // /O picks the right optimization per media: defrag for HDD, retrim for SSD.
        RunOperationAsync($"Optimizing {volume.Letter}", "defrag", $"{volume.Letter} /O", null, restorePoint: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task MaintainAll()
    {
        if (IsRunning) return;
        IsRunning = true;
        CurrentOperation = "One-click maintenance";
        _cts = new CancellationTokenSource();

        _output.Clear();
        AppendLine("=== One-click maintenance ===");
        OutputText = _output.ToString();

        long bytesFreed = 0;
        try
        {
            if (await _backup.ConfirmRestorePointAsync("one-click disk maintenance"))
            {
                AppendLine("Creating a restore point first…");
                var (ok, message) = await _restore.CreateRestorePointAsync("Before SystemCare — one-click disk maintenance");
                AppendLine(ok ? "Restore point created." : message);
            }

            foreach (var v in Volumes.ToList())
            {
                if (_cts.IsCancellationRequested) break;
                AppendLine($"--- Optimizing {v.Letter} (TRIM/defrag) ---");
                await _service.RunAsync("defrag", $"{v.Letter} /O", AppendLine, null, _cts.Token);
            }

            foreach (var v in Volumes.ToList())
            {
                if (_cts.IsCancellationRequested) break;
                AppendLine($"--- Checking {v.Letter} (read-only) ---");
                await _service.RunAsync("chkdsk", v.Letter, AppendLine, null, _cts.Token);
            }

            if (!_cts.IsCancellationRequested)
            {
                AppendLine("--- Cleaning junk files ---");
                var result = await _maintenance.RunMaintenanceNowAsync();
                bytesFreed = result.BytesRemoved + result.BytesFreed;
                AppendLine($"Removed {ByteFormatter.Format(result.BytesRemoved)} of junk in {result.FilesRemoved} file(s).");
            }

            AppendLine(_cts.IsCancellationRequested ? "=== Cancelled ===" : "=== Maintenance complete ===");
            if (!_cts.IsCancellationRequested)
                _history.Record("Disk maintenance", "One-click optimize + clean", bytesFreed, 0, "HardDrive24");
        }
        catch (Exception ex)
        {
            AppendLine($"Error: {ex.Message}");
        }
        finally
        {
            FlushOutput();
            IsRunning = false;
            CurrentOperation = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunSfc() =>
        RunOperationAsync("Scanning system files (SFC)", "sfc", "/scannow", Encoding.Unicode, restorePoint: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunDism() =>
        RunOperationAsync("Repairing Windows image (DISM)", "Dism.exe", "/Online /Cleanup-Image /RestoreHealth", null, restorePoint: true);

    private bool CanRun() => !IsRunning;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunOperationAsync(string title, string fileName, string arguments, Encoding? encoding, bool restorePoint)
    {
        if (IsRunning) return;
        IsRunning = true;
        CurrentOperation = title;
        _cts = new CancellationTokenSource();

        _output.Clear();
        AppendLine($"=== {title} ===");
        OutputText = _output.ToString();

        try
        {
            if (restorePoint && await _backup.ConfirmRestorePointAsync(title))
            {
                AppendLine("Creating a restore point first…");
                var (ok, message) = await _restore.CreateRestorePointAsync($"Before SystemCare — {title}");
                AppendLine(ok ? "Restore point created." : message);
            }

            int exit = await _service.RunAsync(fileName, arguments, AppendLine, encoding, _cts.Token);
            AppendLine(_cts.IsCancellationRequested
                ? "=== Cancelled ==="
                : $"=== Finished (exit code {exit}) ===");
        }
        catch (Exception ex)
        {
            AppendLine($"Error: {ex.Message}");
        }
        finally
        {
            FlushOutput();
            IsRunning = false;
            CurrentOperation = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void AppendLine(string line)
    {
        // Called from a background thread; marshal to the UI thread.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _output.AppendLine(line);
            if (_output.Length > MaxOutputChars)
                _output.Remove(0, _output.Length - MaxOutputChars);
            OutputText = _output.ToString();
        });
    }

    private void FlushOutput() =>
        Application.Current?.Dispatcher.Invoke(() => OutputText = _output.ToString());
}
