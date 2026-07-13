using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class RepairToolkitViewModel : ObservableObject
{
    private const int MaxOutputChars = 60_000;
    private readonly ISystemRepairService _repair;
    private readonly IBackupConfirmationService _backup;
    private readonly IRestorePointService _restore;
    private readonly IHistoryService _history;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> AvailableDrives { get; } = [];

    [ObservableProperty] private string _selectedDrive = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSfcCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDismCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunChkdskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAllCommand))]
    private bool _isRunning;
    [ObservableProperty] private string _currentOperation = "";
    [ObservableProperty] private string _outputText = "Run a repair step to see live output here.";
    [ObservableProperty] private string _sfcStatus = "Not run yet.";
    [ObservableProperty] private string _dismStatus = "Not run yet.";
    [ObservableProperty] private string _chkdskStatus = "Not run yet.";

    // Windows Search index health (2.16)
    private readonly ISearchIndexService _searchIndex;
    [ObservableProperty] private string _indexStatusText = "Reading the search-index size…";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildIndexCommand))]
    private bool _indexBusy;

    public RepairToolkitViewModel(ISystemRepairService repair, IBackupConfirmationService backup,
        IRestorePointService restore, IHistoryService history, ISearchIndexService searchIndex)
    {
        _repair = repair;
        _backup = backup;
        _restore = restore;
        _history = history;
        _searchIndex = searchIndex;
    }

    private async void RefreshIndexStatusAsync()
    {
        try
        {
            var status = await _searchIndex.GetStatusAsync();
            IndexStatusText = status.IndexBytes > 0
                ? $"Index database: {Helpers.ByteFormatter.Format(status.IndexBytes)}" +
                  (status.ServiceRunning ? "" : " — Windows Search service is NOT running") +
                  ". Rebuild if search feels broken or the index has ballooned."
                : "Search index not found — Windows Search may be disabled on this system.";
        }
        catch (Exception)
        {
            // async void: contain; the card just keeps its placeholder text.
            IndexStatusText = "Couldn't read the search-index status.";
        }
    }

    private bool CanRebuildIndex() => !IndexBusy;

    [RelayCommand(CanExecute = nameof(CanRebuildIndex))]
    private async Task RebuildIndexAsync()
    {
        IndexBusy = true;
        try
        {
            var (_, message) = await _searchIndex.RebuildAsync();
            IndexStatusText = message;
        }
        finally
        {
            IndexBusy = false;
        }
    }

    [RelayCommand]
    private void OpenIndexingOptions() => _searchIndex.OpenIndexingOptions();

    public void OnNavigatedTo()
    {
        RefreshIndexStatusAsync();
        if (AvailableDrives.Count > 0) return;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    AvailableDrives.Add(drive.Name.TrimEnd('\\'));
            }
            catch (Exception) { }
        }

        string systemDrive = Environment.SystemDirectory.Length >= 2 ? Environment.SystemDirectory[..2] : "";
        SelectedDrive = AvailableDrives.Contains(systemDrive) ? systemDrive : AvailableDrives.FirstOrDefault() ?? "";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunSfc() => RunStepAsync("Scanning system files (SFC)", async (output, ct) =>
    {
        var result = await _repair.RunSfcAsync(output, ct);
        SfcStatus = result.Summary;
        return result;
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunDism() => RunStepAsync("Repairing the Windows image (DISM)", async (output, ct) =>
    {
        var result = await _repair.RunDismAsync(output, ct);
        DismStatus = result.Summary;
        return result;
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunChkdsk() => RunStepAsync($"Checking {SelectedDrive} for errors (CHKDSK)", async (output, ct) =>
    {
        var result = await _repair.RunChkdskRepairAsync(SelectedDrive, output, ct);
        ChkdskStatus = result.Summary;
        return result;
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAll()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _output.Clear();
        AppendLine("=== Full repair sequence: SFC -> DISM -> CHKDSK ===");
        OutputText = _output.ToString();

        try
        {
            if (await _backup.ConfirmRestorePointAsync("running the full repair sequence"))
            {
                AppendLine("Creating a restore point first…");
                var (ok, message) = await _restore.CreateRestorePointAsync("Before SystemCare — full repair sequence");
                AppendLine(ok ? "Restore point created." : message);
            }

            CurrentOperation = "Scanning system files (SFC)";
            AppendLine($"--- {CurrentOperation} ---");
            var sfc = await _repair.RunSfcAsync(AppendLine, _cts.Token);
            SfcStatus = sfc.Summary;
            AppendLine(sfc.Summary);

            if (!_cts.IsCancellationRequested)
            {
                CurrentOperation = "Repairing the Windows image (DISM)";
                AppendLine($"--- {CurrentOperation} ---");
                var dism = await _repair.RunDismAsync(AppendLine, _cts.Token);
                DismStatus = dism.Summary;
                AppendLine(dism.Summary);
            }

            if (!_cts.IsCancellationRequested && !string.IsNullOrEmpty(SelectedDrive))
            {
                CurrentOperation = $"Checking {SelectedDrive} for errors (CHKDSK)";
                AppendLine($"--- {CurrentOperation} ---");
                var chkdsk = await _repair.RunChkdskRepairAsync(SelectedDrive, AppendLine, _cts.Token);
                ChkdskStatus = chkdsk.Summary;
                AppendLine(chkdsk.Summary);
            }

            AppendLine(_cts.IsCancellationRequested ? "=== Cancelled ===" : "=== Repair sequence complete ===");
            if (!_cts.IsCancellationRequested)
                _history.Record("System repair", "Ran the full SFC + DISM + CHKDSK sequence", icon: "Wrench24");
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

    private bool CanRun() => !IsRunning;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunStepAsync(string title, Func<Action<string>, CancellationToken, Task<RepairResult>> step)
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
            if (await _backup.ConfirmRestorePointAsync(title))
            {
                AppendLine("Creating a restore point first…");
                var (ok, message) = await _restore.CreateRestorePointAsync($"Before SystemCare — {title}");
                AppendLine(ok ? "Restore point created." : message);
            }

            var result = await step(AppendLine, _cts.Token);
            AppendLine(_cts.IsCancellationRequested ? "=== Cancelled ===" : $"=== {result.Summary} ===");
            if (!_cts.IsCancellationRequested)
                _history.Record("System repair", title, icon: "Wrench24");
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
