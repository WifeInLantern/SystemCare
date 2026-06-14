using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.Views.Dialogs;
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

public partial class LeftoverItemViewModel(LeftoverItem item) : ObservableObject
{
    public LeftoverItem Item { get; } = item;
    public string DisplayPath => Item.DisplayPath;
    public string Reason => Item.Reason;
    public string SizeText => Item.SizeBytes > 0 ? ByteFormatter.Format(Item.SizeBytes) : "";
    public string KindText => Item.Kind switch
    {
        LeftoverKind.Folder => "Folder",
        LeftoverKind.Shortcut => "Shortcut",
        LeftoverKind.RegistryKey or LeftoverKind.RegistryValue => "Registry",
        _ => "",
    };

    [ObservableProperty] private bool _isSelected = true;
}

public partial class SoftwareUninstallerViewModel : ObservableObject
{
    private readonly IInstalledAppsService _apps;
    private readonly IFileOperationService _fileOps;
    private readonly ILeftoverScanService _leftovers;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private List<InstalledAppViewModel> _all = [];

    public ObservableCollection<InstalledAppViewModel> Items { get; } = [];

    /// <summary>Leftovers surfaced for review in the post-uninstall dialog.</summary>
    public ObservableCollection<LeftoverItemViewModel> Leftovers { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _headerText = "";

    public SoftwareUninstallerViewModel(
        IInstalledAppsService apps,
        IFileOperationService fileOps,
        ILeftoverScanService leftovers,
        ISnackbarService snackbar,
        IContentDialogService dialogs)
    {
        _apps = apps;
        _fileOps = fileOps;
        _leftovers = leftovers;
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
            // Capture candidate leftovers while the app's registry data is still present.
            var plan = await Task.Run(() => _leftovers.CaptureCandidates(item.App));

            bool launched = await _apps.UninstallAsync(item.App);
            if (!launched)
            {
                _snackbar.Show("Uninstall failed", $"Could not start the uninstaller for {item.Name}.",
                    ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
                return;
            }

            await SearchAndRemoveLeftoversAsync(item.Name, plan);

            await RefreshAsync();
            _snackbar.Show("Uninstall complete", $"{item.Name} was removed.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Verifies which candidates the uninstaller left behind, then lets the user review and remove them.</summary>
    private async Task SearchAndRemoveLeftoversAsync(string appName, LeftoverPlan plan)
    {
        var found = await _leftovers.VerifyAsync(plan, CancellationToken.None);
        if (found.Count == 0) return;

        Leftovers.Clear();
        foreach (var leftover in found) Leftovers.Add(new LeftoverItemViewModel(leftover));

        long totalBytes = found.Sum(f => f.SizeBytes);
        HeaderText = $"{appName}'s uninstaller left {found.Count} item(s) behind" +
                     (totalBytes > 0 ? $" ({ByteFormatter.Format(totalBytes)})." : ".") +
                     " Files go to the Recycle Bin and registry entries are backed up first — both are recoverable.";

        var review = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Leftovers found",
            Content = new LeftoverReviewView { DataContext = this },
            PrimaryButtonText = "Remove selected",
            CloseButtonText = "Keep all",
        });
        if (review != ContentDialogResult.Primary) return;

        var selected = Leftovers.Where(l => l.IsSelected).Select(l => l.Item).ToList();
        if (selected.Count == 0) return;

        var result = await _leftovers.RemoveAsync(selected, null, CancellationToken.None);

        var parts = new List<string>();
        if (result.FilesRemoved > 0)
            parts.Add($"{result.FilesRemoved} file(s)/folder(s) to the Recycle Bin" +
                      (result.BytesRemoved > 0 ? $" ({ByteFormatter.Format(result.BytesRemoved)})" : ""));
        if (result.RegistryRemoved > 0)
            parts.Add($"{result.RegistryRemoved} registry entr(ies) backed up & removed");
        if (result.Skipped > 0)
            parts.Add($"{result.Skipped} skipped");

        _snackbar.Show("Leftovers removed",
            parts.Count > 0 ? string.Join(", ", parts) + "." : "Nothing was removed.",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
    }

    public async void OnNavigatedTo()
    {
        if (_all.Count == 0) await RefreshAsync();
    }
}
