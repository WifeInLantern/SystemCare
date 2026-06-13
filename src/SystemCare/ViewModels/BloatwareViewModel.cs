using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class AppPackageItemViewModel(AppPackage package) : ObservableObject
{
    public AppPackage Package { get; } = package;
    public string DisplayName => Package.DisplayName;
    public string Name => Package.Name;
    public string Publisher => Package.Publisher;
    public bool IsBloatware => Package.IsBloatware;
    [ObservableProperty] private bool _isSelected;
}

public partial class BloatwareViewModel : ObservableObject
{
    private readonly IAppPackageService _apps;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private List<AppPackageItemViewModel> _all = [];

    public ObservableCollection<AppPackageItemViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _bloatwareOnly;
    [ObservableProperty] private string _statusText = "";

    public BloatwareViewModel(IAppPackageService apps, ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _apps = apps;
        _snackbar = snackbar;
        _dialogs = dialogs;
    }

    public async void OnNavigatedTo()
    {
        if (_all.Count == 0) await RefreshAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnBloatwareOnlyChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var packages = await _apps.GetPackagesAsync();
            _all = packages.Select(p => new AppPackageItemViewModel(p)).ToList();
            ApplyFilter();
            StatusText = $"{_all.Count} removable app(s) · {_all.Count(i => i.IsBloatware)} flagged as bloatware.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<AppPackageItemViewModel> filtered = _all;
        if (BloatwareOnly) filtered = filtered.Where(i => i.IsBloatware);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string t = SearchText.Trim();
            filtered = filtered.Where(i =>
                i.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                i.Publisher.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        Items.Clear();
        foreach (var i in filtered) Items.Add(i);
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        var selected = _all.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Remove selected apps?",
            Content = $"{selected.Count} app(s) will be uninstalled for your account.\n\n" +
                      "Some apps may reappear after a major Windows update.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsBusy = true;
        try
        {
            int removed = 0, failed = 0;
            foreach (var item in selected)
            {
                StatusText = $"Removing {item.DisplayName}…";
                var (ok, _) = await _apps.UninstallAsync(item.Package);
                if (ok) { removed++; _all.Remove(item); }
                else failed++;
            }
            ApplyFilter();
            StatusText = $"Removed {removed} app(s)." + (failed > 0 ? $" {failed} could not be removed." : "");
            _snackbar.Show("Apps removed", StatusText, ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
