using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class WindowsUpdateItemViewModel(WindowsUpdateItem item) : ObservableObject
{
    public WindowsUpdateItem Item { get; } = item;
    public string Title => Item.Title;
    public string SubText { get; } =
        string.Join("  ·  ", new[] { item.Kb, item.Category }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string SizeText => Item.SizeBytes > 0 ? ByteFormatter.Format(Item.SizeBytes) : "";
    [ObservableProperty] private bool _isSelected = true;
}

public partial class WindowsUpdateViewModel : ObservableObject
{
    private readonly IWindowsUpdateService _wu;
    private readonly IRestorePointService _restore;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly IHistoryService _history;
    private readonly ILogService _log;

    public ObservableCollection<WindowsUpdateItemViewModel> Updates { get; } = [];
    public ObservableCollection<WindowsUpdateHistoryItem> History { get; } = [];

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _pauseStatus = "";

    public WindowsUpdateViewModel(IWindowsUpdateService wu, IRestorePointService restore, ISettingsService settings,
        ISnackbarService snackbar, IContentDialogService dialogs, IHistoryService history, ILogService log)
    {
        _wu = wu;
        _restore = restore;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _history = history;
        _log = log;
    }

    public async void OnNavigatedTo()
    {
        // async void: an unobserved exception here would crash the process, so contain it.
        try
        {
            if (!HasChecked && !IsChecking) await CheckAsync(CancellationToken.None);
            if (History.Count == 0) await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _log.Warn("WindowsUpdate", "Initial load failed: " + ex.Message);
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CheckAsync(CancellationToken ct)
    {
        IsChecking = true;
        StatusText = "Checking for Windows updates…";
        try
        {
            var found = await _wu.CheckAsync(ct);
            Updates.Clear();
            foreach (var u in found) Updates.Add(new WindowsUpdateItemViewModel(u));
            HasChecked = true;
            StatusText = found.Count == 0
                ? "Windows is up to date."
                : $"{found.Count} update(s) available.";
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
        var selected = Updates.Where(u => u.IsSelected).Select(u => u.Item).ToList();
        if (selected.Count == 0)
        {
            _snackbar.Show("Nothing selected", "Tick at least one update to install.",
                ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
            return;
        }

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Install selected updates?",
            Content = $"{selected.Count} Windows update(s) will be downloaded and installed."
                + (_settings.Current.CreateRestorePointBeforeMaintenance ? "\n\nA system restore point will be created first." : "")
                + "\n\nSome updates may require a restart to finish.",
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsInstalling = true;
        InstallProgress = 0;
        _log.Info("WindowsUpdate", $"Installing {selected.Count} update(s).");
        try
        {
            if (_settings.Current.CreateRestorePointBeforeMaintenance)
            {
                StatusText = "Creating a restore point…";
                await _restore.CreateRestorePointAsync("Before SystemCare Windows updates");
            }

            var progress = new Progress<WindowsUpdateProgress>(p =>
            {
                InstallProgress = p.Percent;
                StatusText = $"Installing {p.Current}/{p.Total}: {p.Title}";
            });

            var result = await _wu.InstallAsync(selected, progress, ct);
            StatusText = result.Message;
            _log.Info("WindowsUpdate", result.Message);
            _snackbar.Show(
                result.Failed == 0 ? "Updates installed" : "Updates finished with errors",
                result.Message,
                result.Failed == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                null, TimeSpan.FromSeconds(7));

            if (result.Installed > 0)
                _history.Record("Windows Update", result.Message, 0, result.Installed, "ArrowSync24");

            await LoadHistoryAsync();
            await CheckAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Install cancelled.";
        }
        finally
        {
            IsInstalling = false;
            InstallProgress = 0;
        }
    }

    [RelayCommand]
    private void Pause(string? days)
    {
        if (!int.TryParse(days, out int d)) d = 7;
        var (ok, message) = _wu.Pause(d);
        PauseStatus = message;
        _snackbar.Show(ok ? "Updates paused" : "Couldn't pause",
            message, ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private void Resume()
    {
        var (ok, message) = _wu.Resume();
        PauseStatus = message;
        _snackbar.Show(ok ? "Updates resumed" : "Couldn't resume",
            message, ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private void OpenWindowsUpdate() => _wu.OpenWindowsUpdate();

    private async Task LoadHistoryAsync()
    {
        var items = await _wu.GetHistoryAsync();
        History.Clear();
        foreach (var h in items) History.Add(h);
    }
}
