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
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly IHistoryService _history;

    public ObservableCollection<SoftwareUpdateItemViewModel> Updates { get; } = [];

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private bool _isWingetMissing;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _updateSummary = "";

    public SoftwareUpdateViewModel(ISoftwareUpdateService software, IRestorePointService restore,
        ISettingsService settings, ISnackbarService snackbar, IContentDialogService dialogs, IHistoryService history)
    {
        _software = software;
        _restore = restore;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _history = history;
    }

    public async void OnNavigatedTo()
    {
        if (!HasChecked && !IsChecking) await CheckAsync(CancellationToken.None);
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
            Updates.Clear();
            foreach (var u in found) Updates.Add(new SoftwareUpdateItemViewModel(u));
            HasChecked = true;
            UpdateSummary = found.Count == 0
                ? "All your apps are up to date."
                : $"{found.Count} app update(s) available.";
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
    private async Task UpdateSelectedAsync(CancellationToken ct)
    {
        var selected = Updates.Where(u => u.IsSelected).Select(u => u.Update).ToList();
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
                + (_settings.Current.CreateRestorePointBeforeMaintenance ? "\n\nA system restore point will be created first." : "")
                + "\n\nSome apps may briefly show their own installer.",
            PrimaryButtonText = "Update",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsUpdating = true;
        InstallProgress = 0;
        try
        {
            if (_settings.Current.CreateRestorePointBeforeMaintenance)
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
}
