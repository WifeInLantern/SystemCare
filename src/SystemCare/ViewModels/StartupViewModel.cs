using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class StartupImpactViewModel(StartupImpact impact)
{
    public string Name => impact.Name;
    public string DurationText => impact.DurationText;
    public string Kind => impact.Kind;

    public Brush LevelBrush { get; } = impact.Level switch
    {
        "High" => Frozen(0xFF, 0x2A, 0x6D),
        "Medium" => Frozen(0xFF, 0xD3, 0x00),
        _ => Frozen(0x00, 0xE5, 0xFF),
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public partial class StartupEntryViewModel(StartupEntry entry, StartupViewModel owner) : ObservableObject
{
    public StartupEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Publisher => Entry.Publisher;
    public string Command => Entry.Command;
    public string SourceDisplay => Entry.SourceDisplay;

    private bool _suppressToggle;

    [ObservableProperty] private bool _isEnabled = entry.IsEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressToggle) return;
        if (!owner.ApplyToggle(this, value))
        {
            // store write failed — revert without re-firing
            _suppressToggle = true;
            IsEnabled = !value;
            _suppressToggle = false;
        }
    }
}

public partial class StartupViewModel : ObservableObject
{
    private readonly IStartupManagerService _startupManager;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly IBootPerformanceService _boot;
    private List<StartupEntryViewModel> _allItems = [];
    private bool _bootLoaded;

    public ObservableCollection<StartupEntryViewModel> Items { get; } = [];
    public ObservableCollection<StartupImpactViewModel> BootApps { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _filterIndex; // 0 all, 1 enabled, 2 disabled
    [ObservableProperty] private bool _showSystemTasks;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _bootSummary = "Reading boot performance…";
    [ObservableProperty] private bool _hasBootApps;

    public StartupViewModel(
        IStartupManagerService startupManager,
        ISettingsService settings,
        ISnackbarService snackbar,
        IContentDialogService dialogs,
        IBootPerformanceService boot)
    {
        _startupManager = startupManager;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _boot = boot;
        _showSystemTasks = settings.Current.ShowSystemTasks;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFilterIndexChanged(int value) => ApplyFilter();

    partial void OnShowSystemTasksChanged(bool value)
    {
        _settings.Current.ShowSystemTasks = value;
        _settings.Save();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _startupManager.GetEntriesAsync(ShowSystemTasks);
            _allItems = entries.Select(e => new StartupEntryViewModel(e, this)).ToList();
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }

        if (!_bootLoaded) { _bootLoaded = true; await LoadBootPerformanceAsync(); }
    }

    private async Task LoadBootPerformanceAsync()
    {
        var report = await _boot.GetAsync();
        BootApps.Clear();
        foreach (var app in report.Apps) BootApps.Add(new StartupImpactViewModel(app));
        HasBootApps = BootApps.Count > 0;
        BootSummary = report.HasBootData
            ? $"Last boot {report.LastBootText} · took {report.BootDurationText} to start · up {report.UptimeText}"
            : $"Last boot {report.LastBootText} · up {report.UptimeText} · (boot timing log not available)";
    }

    private void ApplyFilter()
    {
        IEnumerable<StartupEntryViewModel> filtered = _allItems;

        if (FilterIndex == 1) filtered = filtered.Where(i => i.IsEnabled);
        else if (FilterIndex == 2) filtered = filtered.Where(i => !i.IsEnabled);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string term = SearchText.Trim();
            filtered = filtered.Where(i =>
                i.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Publisher.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Command.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Items.Clear();
        foreach (var item in filtered) Items.Add(item);

        int enabled = _allItems.Count(i => i.IsEnabled);
        SummaryText = $"{_allItems.Count} startup items · {enabled} enabled · {_allItems.Count - enabled} disabled";
    }

    /// <summary>Called by row toggles; returns false when the underlying store write failed.</summary>
    public bool ApplyToggle(StartupEntryViewModel item, bool enabled)
    {
        bool ok = _startupManager.SetEnabled(item.Entry, enabled);
        if (!ok)
        {
            _snackbar.Show("Could not change startup item",
                $"\"{item.Name}\" could not be {(enabled ? "enabled" : "disabled")}.",
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
        }
        else
        {
            int count = _allItems.Count(i => i.IsEnabled);
            SummaryText = $"{_allItems.Count} startup items · {count} enabled · {_allItems.Count - count} disabled";
        }
        return ok;
    }

    [RelayCommand]
    private async Task DeleteAsync(StartupEntryViewModel item)
    {
        var result = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Delete startup entry?",
            Content = $"\"{item.Name}\" will be permanently removed from {item.SourceDisplay}.\n\n" +
                      "The program itself is not uninstalled — only its autostart entry is removed.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
        });
        if (result != ContentDialogResult.Primary) return;

        if (_startupManager.DeleteEntry(item.Entry))
        {
            _allItems.Remove(item);
            Items.Remove(item);
            _snackbar.Show("Entry deleted", $"\"{item.Name}\" no longer starts with Windows.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
        }
        else
        {
            _snackbar.Show("Delete failed", $"\"{item.Name}\" could not be removed.",
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
        }
    }
}
