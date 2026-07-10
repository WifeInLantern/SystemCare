using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class SoftwareUpdateItemViewModel(SoftwareUpdate update) : ObservableObject
{
    public SoftwareUpdate Update { get; } = update;
    public string Name => Update.Name;
    public string Id => Update.Id;
    public string VersionChangeText => Update.VersionChangeText;
    public string Source => Update.Source;
    [ObservableProperty] private bool _isSelected = true;
}

public partial class SoftwareUpdateViewModel : ObservableObject
{
    private readonly ISoftwareUpdateService _software;
    private readonly IRestorePointService _restore;
    private readonly IBackupConfirmationService _backup;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly IHistoryService _history;
    private readonly ILogService _log;

    public ObservableCollection<SoftwareUpdateItemViewModel> Updates { get; } = [];

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private bool _isWingetMissing;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _updateSummary = "";
    [ObservableProperty] private int _excludedCount;

    public SoftwareUpdateViewModel(ISoftwareUpdateService software, IRestorePointService restore,
        ISettingsService settings, ISnackbarService snackbar, IContentDialogService dialogs,
        IHistoryService history, ILogService log, IBackupConfirmationService backup)
    {
        _software = software;
        _restore = restore;
        _backup = backup;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _history = history;
        _log = log;
    }

    private HashSet<string> ExcludedIds =>
        new(_settings.Current.SoftwareUpdateExclusions, StringComparer.OrdinalIgnoreCase);

    public async void OnNavigatedTo()
    {
        try
        {
            if (!HasChecked && !IsChecking) await CheckAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Warn("SoftwareUpdate", "Initial load failed: " + ex.Message);
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CheckAsync(CancellationToken ct)
    {
        IsChecking = true;
        StatusText = "Checking for app updates…";
        try
        {
            if (!await _software.IsAvailableAsync(ct))
            {
                IsWingetMissing = true;
                StatusText = "Windows Package Manager (winget) isn't available.";
                return;
            }
            IsWingetMissing = false;

            var found = await _software.GetUpgradesAsync(ct);
            var excluded = ExcludedIds;
            var visible = found.Where(u => !excluded.Contains(u.Id)).ToList();
            ExcludedCount = found.Count - visible.Count;

            Updates.Clear();
            foreach (var u in visible) Updates.Add(new SoftwareUpdateItemViewModel(u));
            HasChecked = true;

            UpdateSummary = visible.Count == 0
                ? "All your apps are up to date."
                : $"{visible.Count} app update(s) available.";
            if (ExcludedCount > 0) UpdateSummary += $" ({ExcludedCount} excluded)";
            StatusText = UpdateSummary;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Update check cancelled.";
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private Task UpdateSelectedAsync(CancellationToken ct) =>
        RunUpgradeAsync(Updates.Where(u => u.IsSelected).Select(u => u.Update).ToList(), ct);

    [RelayCommand(IncludeCancelCommand = true)]
    private Task UpdateAllAsync(CancellationToken ct) =>
        RunUpgradeAsync(Updates.Select(u => u.Update).ToList(), ct);

    private async Task RunUpgradeAsync(List<SoftwareUpdate> selected, CancellationToken ct)
    {
        if (selected.Count == 0)
        {
            _snackbar.Show("Nothing selected", "Tick at least one app to update.",
                ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
            return;
        }

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Update selected apps?",
            Content = $"{selected.Count} app(s) will be updated via winget."
                + (_settings.Current.CreateRestorePointBeforeMaintenance ? "\n\nYou can choose to create a restore point first." : "")
                + "\n\nSome apps may briefly show their own installer.",
            PrimaryButtonText = "Update",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsUpdating = true;
        InstallProgress = 0;
        _log.Info("SoftwareUpdate", $"Starting update of {selected.Count} app(s).");
        try
        {
            if (await _backup.ConfirmRestorePointAsync("updating your apps"))
            {
                StatusText = "Creating a restore point…";
                await _restore.CreateRestorePointAsync("Before SystemCare app updates");
            }

            var progress = new Progress<SoftwareUpdateProgress>(p =>
            {
                InstallProgress = p.Percent;
                StatusText = $"Updating {p.Current}/{p.Total}: {p.Name}";
            });

            var result = await _software.UpgradeAsync(selected, progress, ct);
            StatusText = result.Message;
            _log.Info("SoftwareUpdate", result.Message);
            _snackbar.Show(
                result.Failed == 0 ? "Updates complete" : "Updates finished with errors",
                result.Message,
                result.Failed == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                null, TimeSpan.FromSeconds(7));

            if (result.Updated > 0)
                _history.Record("Software update", result.Message, 0, result.Updated, "ArrowDownload24");

            await CheckAsync(CancellationToken.None); // refresh; updated apps drop off the list
        }
        catch (OperationCanceledException)
        {
            StatusText = "Update cancelled.";
        }
        finally
        {
            IsUpdating = false;
            InstallProgress = 0;
        }
    }

    /// <summary>Hide an app from this and future update checks (persisted to settings).</summary>
    [RelayCommand]
    private void Exclude(SoftwareUpdateItemViewModel? item)
    {
        if (item is null) return;
        if (!_settings.Current.SoftwareUpdateExclusions.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Current.SoftwareUpdateExclusions.Add(item.Id);
            _settings.Save();
            _log.Info("SoftwareUpdate", $"Excluded {item.Id} from updates.");
        }
        Updates.Remove(item);
        ExcludedCount++;
        UpdateSummary = Updates.Count == 0
            ? "All your apps are up to date."
            : $"{Updates.Count} app update(s) available.";
        UpdateSummary += $" ({ExcludedCount} excluded)";
        StatusText = UpdateSummary;
    }

    /// <summary>Clear the exclusion list and re-check.</summary>
    [RelayCommand]
    private async Task ResetExclusionsAsync()
    {
        _settings.Current.SoftwareUpdateExclusions.Clear();
        _settings.Save();
        _log.Info("SoftwareUpdate", "Cleared software-update exclusions.");
        await CheckAsync(CancellationToken.None);
    }
}
