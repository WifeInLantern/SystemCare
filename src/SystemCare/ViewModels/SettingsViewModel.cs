using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IScheduledMaintenanceService _maintenance;
    private readonly IStartupLauncherService _startup;
    private readonly IUpdateService _updates;
    private readonly IRestorePointService _restore;
    private readonly ISnackbarService _snackbar;
    private readonly ILogService _log;
    private readonly ITrayIconService _tray;
    private readonly IMiniMonitorService _miniMonitor;

    [ObservableProperty] private int _skipTempNewerThanHours;
    [ObservableProperty] private int _largeFileMinMB;
    [ObservableProperty] private int _largeFileTopN;

    [ObservableProperty] private bool _autoMaintenanceEnabled;
    [ObservableProperty] private bool _isWeekly;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _createRestorePointBeforeMaintenance;
    [ObservableProperty] private bool _reduceMotion;
    [ObservableProperty] private bool _showTrayStats;
    [ObservableProperty] private bool _showMiniMonitor;

    [ObservableProperty] private bool _checkForUpdatesOnStartup;
    [ObservableProperty] private string _updateGitHubToken = "";
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _downloadProgress;

    [ObservableProperty] private bool _qaScanFix;
    [ObservableProperty] private bool _qaFreeRam;
    [ObservableProperty] private bool _qaFlushDns;
    [ObservableProperty] private bool _qaEmptyBin;
    [ObservableProperty] private bool _qaRestorePoint;
    private bool _loadingQuickActions;

    public ObservableCollection<string> Exclusions { get; }
    public ObservableCollection<string> CustomFolders { get; }

    public string VersionText { get; }
    public string ElevationText { get; }

    public SettingsViewModel(ISettingsService settings, IScheduledMaintenanceService maintenance,
        IStartupLauncherService startup, IUpdateService updates, IRestorePointService restore,
        ISnackbarService snackbar, ILogService log, ITrayIconService tray, IMiniMonitorService miniMonitor)
    {
        _settings = settings;
        _maintenance = maintenance;
        _startup = startup;
        _updates = updates;
        _restore = restore;
        _snackbar = snackbar;
        _log = log;
        _tray = tray;
        _miniMonitor = miniMonitor;
        _skipTempNewerThanHours = settings.Current.SkipTempNewerThanHours;
        _largeFileMinMB = settings.Current.LargeFileMinMB;
        _largeFileTopN = settings.Current.LargeFileTopN;
        _autoMaintenanceEnabled = settings.Current.AutoMaintenanceEnabled;
        _isWeekly = settings.Current.MaintenanceFrequency != "Daily";
        _minimizeToTray = settings.Current.MinimizeToTray;
        _startWithWindows = settings.Current.StartWithWindows;
        _createRestorePointBeforeMaintenance = settings.Current.CreateRestorePointBeforeMaintenance;
        _reduceMotion = settings.Current.ReduceMotion;
        _showTrayStats = settings.Current.ShowTrayStats;
        _showMiniMonitor = settings.Current.ShowMiniMonitor;
        _checkForUpdatesOnStartup = settings.Current.CheckForUpdatesOnStartup;
        _updateGitHubToken = settings.Current.UpdateGitHubToken;
        Exclusions = new ObservableCollection<string>(settings.Current.CleanupExclusions);
        CustomFolders = new ObservableCollection<string>(settings.Current.CustomJunkFolders);

        _loadingQuickActions = true;
        var qa = settings.Current.DashboardQuickActions;
        _qaScanFix = qa.Contains("scanfix");
        _qaFreeRam = qa.Contains("freeram");
        _qaFlushDns = qa.Contains("flushdns");
        _qaEmptyBin = qa.Contains("emptybin");
        _qaRestorePoint = qa.Contains("restorepoint");
        _loadingQuickActions = false;

        VersionText = $"SystemCare {_updates.CurrentVersion}";
        UpdateStatus = settings.Current.LastUpdateCheckUtc is DateTime t
            ? $"Last checked {t.ToLocalTime():g}"
            : "Not checked yet.";

        using var identity = WindowsIdentity.GetCurrent();
        bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        ElevationText = elevated
            ? "Running with administrator rights — all cleaning locations are available."
            : "Not elevated — system-wide locations may be skipped.";
    }

    partial void OnCheckForUpdatesOnStartupChanged(bool value)
    {
        _settings.Current.CheckForUpdatesOnStartup = value;
        _settings.Save();
    }

    partial void OnShowTrayStatsChanged(bool value)
    {
        _settings.Current.ShowTrayStats = value;
        _settings.Save();
        _tray.EnableLiveStats(value);
    }

    partial void OnShowMiniMonitorChanged(bool value)
    {
        // MiniMonitorService is the source of truth for the ShowMiniMonitor setting; just drive the window.
        if (value) _miniMonitor.Show();
        else _miniMonitor.Hide();
    }

    partial void OnUpdateGitHubTokenChanged(string value)
    {
        _settings.Current.UpdateGitHubToken = value?.Trim() ?? "";
        _settings.Save();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Checking…";
        try
        {
            var update = await _updates.CheckAsync();
            UpdateAvailable = update is not null;
            if (update is not null)
            {
                UpdateStatus = $"Update available: SystemCare {update.Version}.";
                _snackbar.Show("Update available", $"SystemCare {update.Version} is available — click Download & install.",
                    ControlAppearance.Info, null, TimeSpan.FromSeconds(5));
            }
            else
            {
                UpdateStatus = $"You're up to date (SystemCare {_updates.CurrentVersion}).";
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstallAsync()
    {
        if (_updates.LatestAvailable is null) return;
        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        UpdateStatus = $"Downloading {_updates.LatestAvailable.AssetName}…";
        var progress = new Progress<double>(p => DownloadProgress = p);
        try
        {
            string? path = await _updates.DownloadAsync(progress, CancellationToken.None);
            if (path is null)
            {
                UpdateStatus = "Download failed. For a private repo, set a GitHub token below (Settings → Updates).";
                _snackbar.Show("Update download failed",
                    "Could not download the installer. If the repo is private, add a token (see Settings).",
                    ControlAppearance.Danger, null, TimeSpan.FromSeconds(7));
                return;
            }
            // Safety net before the installer replaces app files.
            if (_settings.Current.CreateRestorePointBeforeMaintenance)
            {
                UpdateStatus = "Creating a restore point…";
                var (ok, msg) = await _restore.CreateRestorePointAsync("Before SystemCare app update");
                if (!ok) _log.Warn("Updater", $"Restore point not created: {msg}");
            }

            UpdateStatus = "Download complete — launching the installer…";
            if (_updates.Launch(path))
            {
                _snackbar.Show("Installer started",
                    "Follow the installer to finish updating. You can close SystemCare.",
                    ControlAppearance.Success, null, TimeSpan.FromSeconds(7));
            }
            else
            {
                UpdateStatus = "The installer didn't start — it's in your Downloads folder if you want to run it manually.";
                _snackbar.Show("Installer didn't start",
                    "The installer was downloaded to your Downloads folder but didn't launch.",
                    ControlAppearance.Caution, null, TimeSpan.FromSeconds(7));
            }
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    partial void OnQaScanFixChanged(bool value) => SaveQuickActions();
    partial void OnQaFreeRamChanged(bool value) => SaveQuickActions();
    partial void OnQaFlushDnsChanged(bool value) => SaveQuickActions();
    partial void OnQaEmptyBinChanged(bool value) => SaveQuickActions();
    partial void OnQaRestorePointChanged(bool value) => SaveQuickActions();

    private void SaveQuickActions()
    {
        if (_loadingQuickActions) return;
        var list = new List<string>();
        if (QaScanFix) list.Add("scanfix");
        if (QaFreeRam) list.Add("freeram");
        if (QaFlushDns) list.Add("flushdns");
        if (QaEmptyBin) list.Add("emptybin");
        if (QaRestorePoint) list.Add("restorepoint");
        _settings.Current.DashboardQuickActions = list;
        _settings.Save();
    }

    partial void OnCreateRestorePointBeforeMaintenanceChanged(bool value)
    {
        _settings.Current.CreateRestorePointBeforeMaintenance = value;
        _settings.Save();
    }

    partial void OnReduceMotionChanged(bool value)
    {
        _settings.Current.ReduceMotion = value;
        _settings.Save();
        Helpers.Animations.ReduceMotion = value; // applies live to looping/ambient animations
    }

    [RelayCommand]
    private void AddExclusion()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to exclude from cleaning" };
        if (dialog.ShowDialog() == true && !Exclusions.Contains(dialog.FolderName))
        {
            Exclusions.Add(dialog.FolderName);
            _settings.Current.CleanupExclusions = [.. Exclusions];
            _settings.Save();
        }
    }

    [RelayCommand]
    private void RemoveExclusion(string path)
    {
        if (Exclusions.Remove(path))
        {
            _settings.Current.CleanupExclusions = [.. Exclusions];
            _settings.Save();
        }
    }

    [RelayCommand]
    private void AddCustomFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a custom folder to clean" };
        if (dialog.ShowDialog() == true && !CustomFolders.Contains(dialog.FolderName))
        {
            CustomFolders.Add(dialog.FolderName);
            _settings.Current.CustomJunkFolders = [.. CustomFolders];
            _settings.Save();
        }
    }

    [RelayCommand]
    private void RemoveCustomFolder(string path)
    {
        if (CustomFolders.Remove(path))
        {
            _settings.Current.CustomJunkFolders = [.. CustomFolders];
            _settings.Save();
        }
    }

    partial void OnSkipTempNewerThanHoursChanged(int value)
    {
        _settings.Current.SkipTempNewerThanHours = Math.Max(0, value);
        _settings.Save();
    }

    partial void OnLargeFileMinMBChanged(int value)
    {
        _settings.Current.LargeFileMinMB = Math.Max(1, value);
        _settings.Save();
    }

    partial void OnLargeFileTopNChanged(int value)
    {
        _settings.Current.LargeFileTopN = Math.Clamp(value, 5, 500);
        _settings.Save();
    }

    partial void OnAutoMaintenanceEnabledChanged(bool value)
    {
        _settings.Current.AutoMaintenanceEnabled = value;
        _settings.Save();
        _maintenance.Sync();
    }

    partial void OnIsWeeklyChanged(bool value)
    {
        _settings.Current.MaintenanceFrequency = value ? "Weekly" : "Daily";
        _settings.Save();
        if (_settings.Current.AutoMaintenanceEnabled) _maintenance.Sync();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settings.Current.MinimizeToTray = value;
        _settings.Save();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settings.Current.StartWithWindows = value;
        _settings.Save();
        _startup.Sync(); // create/remove the elevated logon task to match
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.SettingsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.SettingsDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(_log.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_log.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
