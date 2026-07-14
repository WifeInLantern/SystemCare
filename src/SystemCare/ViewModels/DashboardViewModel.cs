using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IJunkScanService _junkScan;
    private readonly IMemoryOptimizerService _memoryOptimizer;
    private readonly IHealthScoreService _healthScore;
    private readonly IStartupManagerService _startupManager;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IRestorePointService _restore;
    private readonly IBackupConfirmationService _backup;
    private readonly INetworkToolsService _network;
    private readonly IHistoryService _history;
    private readonly ISecurityCheckService _security;
    private readonly IRecycleBinService _recycleBin;
    private readonly IHealthTrendService _healthTrend;
    private readonly IDriveTrendService _driveTrend;
    private readonly ITemperatureService _temperature;

    // Storage Forecast (2.14): recorded/computed once per session, not on every 5s drive refresh.
    private bool _driveTrendRecorded;
    private readonly Dictionary<string, string?> _driveForecasts = new(StringComparer.OrdinalIgnoreCase);

    private const int HistorySize = 60;
    private readonly DispatcherTimer _timer;
    private int _tick;
    private JunkScanResult? _lastScan;
    private readonly Queue<double> _cpuSamples = new();
    private readonly Queue<double> _ramSamples = new();
    private readonly Queue<double> _netSamples = new();
    private bool _tempReadInFlight;

    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private double _ramUsedBytes;
    [ObservableProperty] private string _ramTotalText = "";
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _ramHistory = [];

    // Live dashboard (2.16.x): network activity card + hero temperature strip.
    [ObservableProperty] private string _netDownText = "—";
    [ObservableProperty] private string _netUpText = "—";
    [ObservableProperty] private IReadOnlyList<double> _netHistory = [];
    [ObservableProperty] private string _tempSummary = "";
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<DriveStat> _drives = [];

    [ObservableProperty] private double _healthScoreValue = -1;
    [ObservableProperty] private string _lastScanSummary = "Run a scan to rate this PC's health.";
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(FixAllCommand))] private bool _canFix;

    // Customizable quick-action tiles (shown when their id is in AppSettings.DashboardQuickActions).
    [ObservableProperty] private bool _showScanFix;
    [ObservableProperty] private bool _showFreeRam;
    [ObservableProperty] private bool _showFlushDns;
    [ObservableProperty] private bool _showEmptyBin;
    [ObservableProperty] private bool _showRestorePoint;
    [ObservableProperty] private bool _anyQuickAction;

    public DashboardViewModel(
        ISystemInfoService systemInfo,
        IJunkScanService junkScan,
        IMemoryOptimizerService memoryOptimizer,
        IHealthScoreService healthScore,
        IStartupManagerService startupManager,
        ISettingsService settings,
        ISnackbarService snackbar,
        IRestorePointService restore,
        IBackupConfirmationService backup,
        INetworkToolsService network,
        IHistoryService history,
        ISecurityCheckService security,
        IRecycleBinService recycleBin,
        IHealthTrendService healthTrend,
        IDriveTrendService driveTrend,
        ITemperatureService temperature)
    {
        _systemInfo = systemInfo;
        _junkScan = junkScan;
        _memoryOptimizer = memoryOptimizer;
        _healthScore = healthScore;
        _startupManager = startupManager;
        _settings = settings;
        _snackbar = snackbar;
        _restore = restore;
        _backup = backup;
        _network = network;
        _history = history;
        _security = security;
        _recycleBin = recycleBin;
        _healthTrend = healthTrend;
        _driveTrend = driveTrend;
        _temperature = temperature;

        if (_settings.Current.LastHealthScore is int saved)
        {
            HealthScoreValue = saved;
            if (_settings.Current.LastScanUtc is DateTime when)
                LastScanSummary = $"Last scan: {when.ToLocalTime():g}";
        }

        ApplyQuickActions();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshStats();
    }

    /// <summary>Re-reads which quick-action tiles to show (called when returning to the dashboard).</summary>
    public void ApplyQuickActions()
    {
        var enabled = _settings.Current.DashboardQuickActions;
        ShowScanFix = enabled.Contains("scanfix");
        ShowFreeRam = enabled.Contains("freeram");
        ShowFlushDns = enabled.Contains("flushdns");
        ShowEmptyBin = enabled.Contains("emptybin");
        ShowRestorePoint = enabled.Contains("restorepoint");
        AnyQuickAction = ShowScanFix || ShowFreeRam || ShowFlushDns || ShowEmptyBin || ShowRestorePoint;
    }

    public void StartMonitoring()
    {
        ApplyQuickActions();
        RefreshStats();
        _timer.Start();
    }

    public void StopMonitoring() => _timer.Stop();

    private void RefreshStats()
    {
        bool includeDrives = _tick++ % 5 == 0;
        var snapshot = _systemInfo.GetSnapshot(includeDrives);

        double cpuValue = snapshot.CpuPercent ?? 0;
        CpuText = snapshot.CpuPercent is double cpu ? $"{cpu:0}%" : "—";
        CpuPercent = cpuValue;
        RamText = $"{ByteFormatter.Format((long)snapshot.RamUsedBytes)} / {ByteFormatter.Format((long)snapshot.RamTotalBytes)}";
        RamUsedBytes = snapshot.RamUsedBytes;
        RamTotalText = $" / {ByteFormatter.Format((long)snapshot.RamTotalBytes)}";
        RamPercent = snapshot.RamLoadPercent;

        PushSample(_cpuSamples, cpuValue);
        PushSample(_ramSamples, snapshot.RamLoadPercent);
        CpuHistory = _cpuSamples.ToArray();
        RamHistory = _ramSamples.ToArray();

        // Live network card (2.16.x): rates come free with the snapshot.
        NetDownText = ByteFormatter.Format((long)snapshot.NetRecvBytesPerSec) + "/s";
        NetUpText = ByteFormatter.Format((long)snapshot.NetSentBytesPerSec) + "/s";
        PushSample(_netSamples, snapshot.NetRecvBytesPerSec + snapshot.NetSentBytesPerSec);
        NetHistory = _netSamples.ToArray();

        // Hero temperature strip (2.16.x): every 10th tick, off the UI thread — LHM reads are
        // heavy and must never stall the 1s sampler. INPC setters are thread-safe for scalars.
        if (_tick % 10 == 1 && !_tempReadInFlight)
        {
            _tempReadInFlight = true;
            _ = Task.Run(() =>
            {
                try
                {
                    var temps = _temperature.Read();
                    double? cpu = temps.FirstOrDefault(t => t.Category == "Processor")?.Celsius;
                    double? gpu = temps.FirstOrDefault(t => t.Category == "Graphics")?.Celsius;
                    TempSummary = (cpu, gpu) switch
                    {
                        (double c, double g) => $"CPU {c:0} °C   ·   GPU {g:0} °C",
                        (double c, null) => $"CPU {c:0} °C",
                        (null, double g) => $"GPU {g:0} °C",
                        _ => "",
                    };
                }
                catch (Exception)
                {
                    // sensors are decorative here — never disturb the dashboard
                }
                finally
                {
                    _tempReadInFlight = false;
                }
            });
        }

        if (includeDrives)
        {
            // Storage Forecast: one sample + one fit per drive per session (history is daily-grained).
            if (!_driveTrendRecorded)
            {
                _driveTrendRecorded = true;
                foreach (var drive in snapshot.Drives)
                {
                    _driveTrend.Record(drive.Name, drive.FreeBytes, drive.TotalBytes);
                    _driveForecasts[drive.Name] = _driveTrend.GetForecastText(drive.Name);
                }
            }

            Drives.Clear();
            foreach (var drive in snapshot.Drives)
            {
                drive.Forecast = _driveForecasts.GetValueOrDefault(drive.Name);
                Drives.Add(drive);
            }
        }
    }

    private static void PushSample(Queue<double> buffer, double value)
    {
        buffer.Enqueue(value);
        while (buffer.Count > HistorySize) buffer.Dequeue();
    }

    private IEnumerable<string> DefaultCategoryIds() =>
        _junkScan.Categories
            .Where(c => _settings.Current.JunkCategoryToggles.GetValueOrDefault(c.Id, c.EnabledByDefault))
            .Select(c => c.Id);

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsWorking = true;
        CanFix = false;
        try
        {
            LastScanSummary = "Scanning for junk files…";
            var junkTask = _junkScan.ScanAsync(DefaultCategoryIds(), null, ct);
            var startupTask = _startupManager.GetEntriesAsync(includeSystemTasks: false);
            var securityTask = _security.GetChecksAsync();
            await Task.WhenAll(junkTask, startupTask, securityTask);

            _lastScan = junkTask.Result;
            int enabledStartup = startupTask.Result.Count(e => e.IsEnabled);
            int securityIssues = securityTask.Result.Count(c =>
                c.Status is Models.SecurityStatus.Warning or Models.SecurityStatus.Bad);
            var snapshot = _systemInfo.GetSnapshot(includeDrives: true);

            var report = _healthScore.Compute(new HealthInputs
            {
                JunkBytes = _lastScan.TotalBytes,
                EnabledStartupItems = enabledStartup,
                RamLoadPercent = snapshot.RamLoadPercent,
                SecurityIssues = securityIssues,
                SystemDriveFreePercent = DriveMetrics.SystemDriveFreePercent(snapshot.Drives),
            });

            HealthScoreValue = report.Score;
            LastScanSummary =
                $"{ByteFormatter.Format(_lastScan.TotalBytes)} of junk in {_lastScan.TotalFiles:N0} files · " +
                $"{enabledStartup} startup items · RAM {snapshot.RamLoadPercent:0}% used" +
                (securityIssues > 0 ? $" · {securityIssues} security issue(s)" : "");
            CanFix = _lastScan.TotalBytes > 0;

            _settings.Current.LastScanUtc = DateTime.UtcNow;
            _settings.Current.LastHealthScore = report.Score;
            _settings.Save();
            _healthTrend.Record(report.Score); // one snapshot/day feeds the Care Report trend
        }
        catch (OperationCanceledException)
        {
            LastScanSummary = "Scan cancelled.";
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task FixAllAsync()
    {
        if (_lastScan is null) return;
        IsWorking = true;
        try
        {
            if (await _backup.ConfirmRestorePointAsync("the one-click fix"))
            {
                LastScanSummary = "Creating a restore point…";
                await _restore.CreateRestorePointAsync("Before SystemCare Fix all");
            }

            LastScanSummary = "Cleaning junk and trimming memory…";
            var cleanTask = _junkScan.CleanAsync(_lastScan, DefaultCategoryIds(), null, CancellationToken.None);
            var ramTask = _memoryOptimizer.OptimizeAsync();
            await Task.WhenAll(cleanTask, ramTask);

            _snackbar.Show("Fix complete",
                $"Removed {ByteFormatter.Format(cleanTask.Result.BytesRemoved)} of junk and freed " +
                $"{ByteFormatter.Format(ramTask.Result.BytesFreed)} of RAM across {ramTask.Result.ProcessesTrimmed} processes.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(6));

            _history.Record("Fix all",
                $"Removed {ByteFormatter.Format(cleanTask.Result.BytesRemoved)} of junk · freed {ByteFormatter.Format(ramTask.Result.BytesFreed)} of RAM",
                cleanTask.Result.BytesRemoved, cleanTask.Result.FilesRemoved, "Rocket24");

            CanFix = false;
            await ScanAsync(CancellationToken.None); // re-score so the gauge reflects the cleanup
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private async Task OptimizeRamAsync()
    {
        var result = await _memoryOptimizer.OptimizeAsync();
        _snackbar.Show("Memory optimized",
            $"Freed {ByteFormatter.Format(result.BytesFreed)} across {result.ProcessesTrimmed} processes.",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private void FlushDns()
    {
        string message = _network.FlushDns();
        _snackbar.Show("DNS cache", message, ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
    }

    [RelayCommand]
    private void EmptyRecycleBin()
    {
        var (bytes, items) = _recycleBin.Query();
        if (items <= 0)
        {
            _snackbar.Show("Recycle Bin", "The Recycle Bin is already empty.", ControlAppearance.Info, null, TimeSpan.FromSeconds(4));
            return;
        }
        _recycleBin.Empty();
        _snackbar.Show("Recycle Bin emptied",
            $"Reclaimed {ByteFormatter.Format(bytes)} from {items:N0} item(s).",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        _snackbar.Show("Restore point", "Creating a restore point…", ControlAppearance.Info, null, TimeSpan.FromSeconds(3));
        var (ok, message) = await _restore.CreateRestorePointAsync($"SystemCare — {DateTime.Now:g}");
        _snackbar.Show(ok ? "Restore point created" : "Restore point",
            message, ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private void NavigateTo(string page)
    {
        if (System.Windows.Application.Current.MainWindow is MainWindow window)
            window.NavigateTo(page);
    }
}
