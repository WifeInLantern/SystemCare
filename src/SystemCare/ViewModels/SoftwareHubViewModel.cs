using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class SoftwareHubItemViewModel(SoftwareHubAppStatus status) : ObservableObject
{
    public SoftwareHubApp App { get; } = status.App;
    public string Name => App.Name;
    public string Id => App.Id;
    public string Category => App.Category;
    public string Description => App.Description;
    public bool IsInstalled { get; } = status.IsInstalled;
    [ObservableProperty] private bool _isSelected;
}

public partial class SoftwareHubViewModel : ObservableObject
{
    private readonly ISoftwareHubService _software;
    private readonly IRestorePointService _restore;
    private readonly IBackupConfirmationService _backup;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly IHistoryService _history;
    private readonly ILogService _log;

    public ObservableCollection<SoftwareHubItemViewModel> Apps { get; } = [];
    /// <summary>Grouped view used by the page (grouped by <see cref="SoftwareHubItemViewModel.Category"/>).</summary>
    public ICollectionView GroupedApps { get; }

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private bool _isWingetMissing;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summaryText = "";

    public SoftwareHubViewModel(ISoftwareHubService software, IRestorePointService restore,
        ISnackbarService snackbar, IContentDialogService dialogs, IHistoryService history,
        ILogService log, IBackupConfirmationService backup)
    {
        _software = software;
        _restore = restore;
        _backup = backup;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _history = history;
        _log = log;

        GroupedApps = CollectionViewSource.GetDefaultView(Apps);
        GroupedApps.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SoftwareHubItemViewModel.Category)));
    }

    public async void OnNavigatedTo()
    {
        if (!HasChecked && !IsChecking) await RefreshAsync(CancellationToken.None);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RefreshAsync(CancellationToken ct)
    {
        IsChecking = true;
        StatusText = "Checking installed apps…";
        try
        {
            if (!await _software.IsAvailableAsync(ct))
            {
                IsWingetMissing = true;
                StatusText = "Windows Package Manager (winget) isn't available.";
                return;
            }
            IsWingetMissing = false;

            var statuses = await _software.GetCatalogAsync(ct);
            Apps.Clear();
            foreach (var s in statuses) Apps.Add(new SoftwareHubItemViewModel(s));
            HasChecked = true;

            SummaryText = $"{Apps.Count(a => a.IsInstalled)} of {Apps.Count} apps already installed.";
            StatusText = SummaryText;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Check cancelled.";
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task InstallSelectedAsync(CancellationToken ct)
    {
        var selected = Apps.Where(a => a.IsSelected && !a.IsInstalled).Select(a => a.App).ToList();
        if (selected.Count == 0)
        {
            _snackbar.Show("Nothing selected", "Tick at least one app to install.",
                ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
            return;
        }

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Install selected apps?",
            Content = $"{selected.Count} app(s) will be installed via winget.\n\nSome installers may briefly show their own window.",
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsInstalling = true;
        InstallProgress = 0;
        _log.Info("SoftwareHub", $"Starting install of {selected.Count} app(s).");
        try
        {
            if (await _backup.ConfirmRestorePointAsync("installing new apps"))
            {
                StatusText = "Creating a restore point…";
                await _restore.CreateRestorePointAsync("Before SystemCare app install");
            }

            var progress = new Progress<SoftwareHubInstallProgress>(p =>
            {
                InstallProgress = p.Percent;
                StatusText = $"Installing {p.Current}/{p.Total}: {p.Name}";
            });

            var result = await _software.InstallAsync(selected, progress, ct);
            StatusText = result.Message;
            _log.Info("SoftwareHub", result.Message);
            _snackbar.Show(
                result.Failed == 0 ? "Install complete" : "Install finished with errors",
                result.Message,
                result.Failed == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                null, TimeSpan.FromSeconds(7));

            if (result.Installed > 0)
                _history.Record("Software install", result.Message, 0, result.Installed, "AppsAddIn24");

            await RefreshAsync(CancellationToken.None); // refresh so newly-installed apps flip to the Installed badge
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
}
