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
    /// <summary>Results of the current winget search (shown instead of the catalog while searching).</summary>
    public ObservableCollection<SoftwareHubItemViewModel> SearchResults { get; } = [];

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallOneCommand))]
    private bool _isInstalling;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private bool _isWingetMissing;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summaryText = "";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private string _searchStatusText = "";

    /// <summary>Catalog is shown once checked and while no search is active.</summary>
    public bool ShowCatalog => HasChecked && !IsSearchMode;

    /// <summary>Keystroke debounce before a search spawns a winget process. Tests set 0.</summary>
    internal int SearchDebounceMs = 400;
    /// <summary>The in-flight debounced search, exposed so tests can await it deterministically.</summary>
    internal Task? ActiveSearchTask;
    private CancellationTokenSource? _searchCts;

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

    partial void OnHasCheckedChanged(bool value) => OnPropertyChanged(nameof(ShowCatalog));
    partial void OnIsSearchModeChanged(bool value) => OnPropertyChanged(nameof(ShowCatalog));

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        if (string.IsNullOrWhiteSpace(value))
        {
            IsSearchMode = false;
            IsSearching = false;
            SearchResults.Clear();
            SearchStatusText = "";
            return;
        }
        var cts = _searchCts = new CancellationTokenSource();
        ActiveSearchTask = DebouncedSearchAsync(value.Trim(), cts.Token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            if (SearchDebounceMs > 0) await Task.Delay(SearchDebounceMs, ct);
            await RunSearchAsync(query, ct);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke or cleared — the newer search owns the UI state
        }
    }

    private async Task RunSearchAsync(string query, CancellationToken ct)
    {
        IsSearchMode = true;
        IsSearching = true;
        SearchStatusText = $"Searching winget for “{query}”…";
        try
        {
            var results = await _software.SearchAsync(query, ct);
            if (ct.IsCancellationRequested) return;

            SearchResults.Clear();
            foreach (var s in results) SearchResults.Add(new SoftwareHubItemViewModel(s));
            SearchStatusText = results.Count == 0
                ? $"No winget packages match “{query}”."
                : $"{results.Count} result(s) for “{query}”.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn("SoftwareHub", $"winget search failed: {ex.Message}");
            SearchStatusText = "Search failed — try again.";
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsSearching = false;
        }
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

        await InstallAppsAsync(selected, ct);
    }

    /// <summary>Installs one app straight from a search-result row.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallOne))]
    private async Task InstallOneAsync(SoftwareHubItemViewModel? item)
    {
        if (item is null || item.IsInstalled) return;
        await InstallAppsAsync([item.App], CancellationToken.None);
    }

    private bool CanInstallOne(SoftwareHubItemViewModel? item) => !IsInstalling;

    private async Task InstallAppsAsync(List<SoftwareHubApp> picks, CancellationToken ct)
    {
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = picks.Count == 1 ? $"Install {picks[0].Name}?" : "Install selected apps?",
            Content = $"{picks.Count} app(s) will be installed via winget.\n\nSome installers may briefly show their own window.",
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        var selected = picks;
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
            if (IsSearchMode && !string.IsNullOrWhiteSpace(SearchText))
                await RunSearchAsync(SearchText.Trim(), CancellationToken.None); // flip badges in search results too
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
