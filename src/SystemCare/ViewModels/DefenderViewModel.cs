using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class DefenderViewModel : ObservableObject
{
    private const int MaxOutputChars = 60_000;
    private readonly IDefenderService _defender;
    private readonly IHistoryService _history;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(FullScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateSignaturesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(FullScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateSignaturesCommand))]
    private bool _isAvailable = true;
    [ObservableProperty] private string _headline = "Reading Defender status…";
    [ObservableProperty] private string _statusIcon = "ShieldCheckmark24";
    [ObservableProperty] private string _realTimeText = "—";
    [ObservableProperty] private string _tamperText = "—";
    [ObservableProperty] private string _signatureText = "—";
    [ObservableProperty] private string _lastQuickScanText = "—";
    [ObservableProperty] private string _lastFullScanText = "—";
    [ObservableProperty] private string _currentOperation = "";
    [ObservableProperty] private string _outputText = "Run a scan to see live output here.";

    public DefenderViewModel(IDefenderService defender, IHistoryService history)
    {
        _defender = defender;
        _history = history;
    }

    public async void OnNavigatedTo() => await RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        var s = await _defender.GetStatusAsync();
        IsAvailable = s.IsAvailable;
        Headline = s.Headline;
        StatusIcon = s.Icon;
        RealTimeText = s.RealTimeProtectionEnabled ? "On" : "Off";
        TamperText = s.TamperProtectionEnabled ? "On" : "Off";
        SignatureText = string.IsNullOrEmpty(s.AntivirusSignatureVersion)
            ? "Unknown"
            : $"{s.AntivirusSignatureVersion} ({s.SignatureAgeDays} day(s) old)";
        LastQuickScanText = FormatDate(s.LastQuickScan);
        LastFullScanText = FormatDate(s.LastFullScan);
    }

    private static string FormatDate(DateTime? dt) =>
        dt is null ? "Never" : dt.Value.ToLocalTime().ToString("g");

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task QuickScan() => RunScanAsync(DefenderScanType.Quick, "Quick scan");

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task FullScan() => RunScanAsync(DefenderScanType.Full, "Full scan");

    private async Task RunScanAsync(DefenderScanType type, string label)
    {
        if (IsRunning) return;
        IsRunning = true;
        CurrentOperation = $"{label} in progress…";
        _cts = new CancellationTokenSource();
        _output.Clear();
        AppendLine($"=== {label} started ===");
        OutputText = _output.ToString();

        try
        {
            var result = await _defender.StartScanAsync(type, AppendLine, _cts.Token);
            AppendLine(result.Summary);
            if (!_cts.IsCancellationRequested)
            {
                _history.Record("Defender", result.Summary, icon: "ShieldCheckmark24");
                await RefreshAsync();
            }
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

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task UpdateSignatures()
    {
        if (IsRunning) return;
        IsRunning = true;
        CurrentOperation = "Updating definitions…";
        _cts = new CancellationTokenSource();
        _output.Clear();
        AppendLine("=== Definition update started ===");
        OutputText = _output.ToString();

        try
        {
            var result = await _defender.UpdateSignaturesAsync(AppendLine, _cts.Token);
            AppendLine(result.Summary);
            if (!_cts.IsCancellationRequested) await RefreshAsync();
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

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenSecurity() => _defender.OpenWindowsSecurity();

    private bool CanRun() => !IsRunning;

    // Scans/updates additionally require Defender to be the active, readable antivirus.
    private bool CanScan() => !IsRunning && IsAvailable;

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
