using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

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

public partial class DiskHealthViewModel : ObservableObject
{
    private const int MaxOutputChars = 60_000;
    private readonly IDiskMaintenanceService _service;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<PhysicalDiskHealth> Disks { get; } = [];
    public ObservableCollection<DiskVolumeViewModel> Volumes { get; } = [];

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSfcCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDismCommand))]
    private bool _isRunning;
    [ObservableProperty] private string _currentOperation = "";
    [ObservableProperty] private string _outputText = "Run a check or maintenance task to see live output here.";

    private readonly IRestorePointService _restore;
    private readonly ISettingsService _settings;

    public DiskHealthViewModel(IDiskMaintenanceService service, IRestorePointService restore, ISettingsService settings)
    {
        _service = service;
        _restore = restore;
        _settings = settings;
    }

    public async void OnNavigatedTo()
    {
        if (Disks.Count > 0 || Volumes.Count > 0) return;
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
            Disks.Clear();
            foreach (var disk in disks) Disks.Add(disk);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task CheckErrorsAsync(DiskVolumeViewModel volume) =>
        // Read-only check (no /f): reports filesystem errors without locking the volume. No restore point needed.
        RunOperationAsync($"Checking {volume.Letter} for errors", "chkdsk", volume.Letter, null, restorePoint: false);

    public Task OptimizeAsync(DiskVolumeViewModel volume) =>
        // /O picks the right optimization per media: defrag for HDD, retrim for SSD.
        RunOperationAsync($"Optimizing {volume.Letter}", "defrag", $"{volume.Letter} /O", null, restorePoint: true);

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
            if (restorePoint && _settings.Current.CreateRestorePointBeforeMaintenance)
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
