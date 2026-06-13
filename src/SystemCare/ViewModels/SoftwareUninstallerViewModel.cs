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

public partial class InstalledAppViewModel(InstalledApp app)
{
    public InstalledApp App { get; } = app;
    public string Name => App.Name;
    public string Publisher => App.Publisher;
    public string Version => App.Version;
    public string SizeText => App.SizeBytes > 0 ? ByteFormatter.Format(App.SizeBytes) : "";
    public string InstallDateText => App.InstallDate?.ToString("d") ?? "";
}

public partial class SoftwareUninstallerViewModel : ObservableObject
{
    private readonly IInstalledAppsService _apps;
    private readonly IFileOperationService _fileOps;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private List<InstalledAppViewModel> _all = [];

    public ObservableCollection<InstalledAppViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _summaryText = "";

    public SoftwareUninstallerViewModel(
        IInstalledAppsService apps,
        IFileOperationService fileOps,
        ISnackbarService snackbar,
        IContentDialogService dialogs)
    {
        _apps = apps;
        _fileOps = fileOps;
        _snackbar = snackbar;
        _dialogs = dialogs;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var apps = await _apps.GetInstalledAppsAsync();
            _all = apps.Select(a => new InstalledAppViewModel(a)).ToList();
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<InstalledAppViewModel> filtered = _all;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string term = SearchText.Trim();
            filtered = filtered.Where(a =>
                a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Items.Clear();
        foreach (var item in filtered) Items.Add(item);

        long totalSize = _all.Sum(a => a.App.SizeBytes);
        SummaryText = $"{_all.Count} programs · {ByteFormatter.Format(totalSize)} total";
    }

    [RelayCommand]
    private void OpenLocation(InstalledAppViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.App.InstallLocation))
            _fileOps.OpenInExplorer(item.App.InstallLocation!);
    }

    [RelayCommand]
    private async Task UninstallAsync(InstalledAppViewModel item)
    {
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = $"Uninstall {item.Name}?",
            Content = "This launches the program's own uninstaller. Follow any prompts it shows.",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsBusy = true;
        try
        {
            bool launched = await _apps.UninstallAsync(item.App);
            if (!launched)
            {
                _snackbar.Show("Uninstall failed", $"Could not start the uninstaller for {item.Name}.",
                    ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
                return;
            }

            // Offer leftover cleanup if the install folder survived.
            string? location = item.App.InstallLocation;
            if (!string.IsNullOrWhiteSpace(location) && Directory.Exists(location))
            {
                var sweep = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = "Remove leftover files?",
                    Content = $"The folder still exists:\n{location}\n\nMove it to the Recycle Bin?",
                    PrimaryButtonText = "Move to Recycle Bin",
                    CloseButtonText = "Keep",
                });
                if (sweep == ContentDialogResult.Primary)
                    _fileOps.SendToRecycleBin(location!);
            }

            await RefreshAsync();
            _snackbar.Show("Uninstall complete", $"{item.Name} was removed.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async void OnNavigatedTo()
    {
        if (_all.Count == 0) await RefreshAsync();
    }
}
