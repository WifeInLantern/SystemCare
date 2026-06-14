using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class DriverUpdateItemViewModel(DriverUpdate update) : ObservableObject
{
    public DriverUpdate Update { get; } = update;
    public string Title => Update.Title;
    public string Manufacturer => Update.Manufacturer;
    public string DriverClass => Update.DriverClass;
    public string DateText => Update.DateText;
    public string SizeText => Update.SizeText;
    [ObservableProperty] private bool _isSelected = true;
}

public partial class DriverUpdateViewModel : ObservableObject
{
    private readonly IDriverUpdateService _drivers;
    private readonly IRestorePointService _restore;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;

    public ObservableCollection<DriverDevice> Devices { get; } = [];
    public ObservableCollection<DriverUpdateItemViewModel> Updates { get; } = [];

    [ObservableProperty] private bool _isLoadingDevices;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _deviceSummary = "";
    [ObservableProperty] private string _updateSummary = "";
    [ObservableProperty] private string _statusText = "";

    public DriverUpdateViewModel(IDriverUpdateService drivers, IRestorePointService restore,
        ISettingsService settings, ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _drivers = drivers;
        _restore = restore;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;
    }

    public async void OnNavigatedTo()
    {
        if (Devices.Count == 0) await RefreshDevicesAsync();
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        IsLoadingDevices = true;
        try
        {
            var devices = await _drivers.GetInstalledDriversAsync();
            Devices.Clear();
            foreach (var d in devices) Devices.Add(d);
            int problems = devices.Count(d => d.HasProblem);
            DeviceSummary = $"{devices.Count} device(s)"
                + (problems > 0 ? $" · {problems} with a driver problem" : " · no driver problems detected");
        }
        finally
        {
            IsLoadingDevices = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CheckForUpdatesAsync(CancellationToken ct)
    {
        IsChecking = true;
        StatusText = "Checking Windows Update for newer drivers…";
        try
        {
            var found = await _drivers.CheckForUpdatesAsync(ct);
            Updates.Clear();
            foreach (var u in found) Updates.Add(new DriverUpdateItemViewModel(u));
            HasChecked = true;
            UpdateSummary = found.Count == 0
                ? "No driver updates available from Windows Update."
                : $"{found.Count} driver update(s) available.";
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
    private async Task InstallSelectedAsync(CancellationToken ct)
    {
        var selected = Updates.Where(u => u.IsSelected).Select(u => u.Update).ToList();
        if (selected.Count == 0)
        {
            _snackbar.Show("Nothing selected", "Tick at least one driver update to install.",
                ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
            return;
        }

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Install selected driver updates?",
            Content = $"{selected.Count} driver update(s) will be downloaded and installed from Windows Update."
                + (_settings.Current.CreateRestorePointBeforeMaintenance
                    ? "\n\nA system restore point will be created first."
                    : "")
                + "\n\nSome drivers may require a restart to finish.",
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsInstalling = true;
        InstallProgress = 0;
        try
        {
            if (_settings.Current.CreateRestorePointBeforeMaintenance)
            {
                StatusText = "Creating a restore point…";
                await _restore.CreateRestorePointAsync("Before SystemCare driver update");
            }

            var progress = new Progress<DriverInstallProgress>(p =>
            {
                InstallProgress = p.Percent;
                StatusText = $"Installing {p.Current}/{p.Total}: {p.Title}";
            });

            var result = await _drivers.InstallAsync(selected, progress, ct);
            StatusText = result.Message;
            _snackbar.Show(
                result.Failed == 0 ? "Driver update complete" : "Driver update finished with errors",
                result.Message,
                result.Failed == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                null, TimeSpan.FromSeconds(7));

            // Refresh inventory and drop the ones we installed.
            await RefreshDevicesAsync();
            await CheckForUpdatesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Driver installation cancelled.";
        }
        finally
        {
            IsInstalling = false;
            InstallProgress = 0;
        }
    }

    [RelayCommand]
    private void OpenWindowsUpdate() => _drivers.OpenWindowsUpdate();

    [RelayCommand]
    private void OpenDeviceManager() => _drivers.OpenDeviceManager();
}
