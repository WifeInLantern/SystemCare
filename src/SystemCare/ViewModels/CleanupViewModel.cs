using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class JunkCategoryItemViewModel(JunkCategory category, ISettingsService settings) : ObservableObject
{
    public JunkCategory Category { get; } = category;
    public string Name => Category.Name;
    public string Description => Category.Description;

    [ObservableProperty] private bool _isSelected =
        settings.Current.JunkCategoryToggles.GetValueOrDefault(category.Id, category.EnabledByDefault);

    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private int _fileCount;

    partial void OnIsSelectedChanged(bool value)
    {
        settings.Current.JunkCategoryToggles[Category.Id] = value;
        settings.Save();
    }
}

public partial class CleanupViewModel : ObservableObject
{
    private readonly IJunkScanService _junkScan;
    private readonly ISnackbarService _snackbar;
    private readonly IHistoryService _history;
    private JunkScanResult? _scanResult;

    public ObservableCollection<JunkCategoryItemViewModel> Categories { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _totalText = "Select categories and scan to see what can be cleaned.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CleanCommand))] private bool _canClean;

    public CleanupViewModel(IJunkScanService junkScan, ISettingsService settings, ISnackbarService snackbar,
        IHistoryService history)
    {
        _junkScan = junkScan;
        _snackbar = snackbar;
        _history = history;
        Categories = new ObservableCollection<JunkCategoryItemViewModel>(
            junkScan.Categories.Select(c => new JunkCategoryItemViewModel(c, settings)));
    }

    private List<string> SelectedIds() => Categories.Where(c => c.IsSelected).Select(c => c.Category.Id).ToList();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsBusy = true;
        CanClean = false;
        foreach (var category in Categories)
        {
            category.SizeText = "";
            category.FileCount = 0;
        }

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressText = string.IsNullOrEmpty(p.CurrentPath) ? "" : p.CurrentPath;
            TotalText = $"Found {ByteFormatter.Format(p.BytesFound)} in {p.FilesFound:N0} files…";
        });

        try
        {
            _scanResult = await _junkScan.ScanAsync(SelectedIds(), progress, ct);

            foreach (var categoryResult in _scanResult.Categories)
            {
                var item = Categories.FirstOrDefault(c => c.Category.Id == categoryResult.Category.Id);
                if (item is null) continue;
                item.SizeText = ByteFormatter.Format(categoryResult.TotalBytes);
                item.FileCount = categoryResult.FileCount;
            }

            TotalText = $"{ByteFormatter.Format(_scanResult.TotalBytes)} of junk found in {_scanResult.TotalFiles:N0} files.";
            CanClean = _scanResult.TotalBytes > 0;
        }
        catch (OperationCanceledException)
        {
            TotalText = "Scan cancelled.";
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (_scanResult is null) return;
        IsBusy = true;
        CanClean = false;

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressText = p.CurrentPath;
            TotalText = $"Removed {ByteFormatter.Format(p.BytesFound)}…";
        });

        try
        {
            var result = await _junkScan.CleanAsync(_scanResult, SelectedIds(), progress, CancellationToken.None);
            TotalText = $"Removed {ByteFormatter.Format(result.BytesRemoved)} ({result.FilesRemoved:N0} files). " +
                        (result.FilesSkipped > 0 ? $"{result.FilesSkipped:N0} in-use files were skipped." : "");
            _snackbar.Show("Cleanup complete",
                $"Removed {ByteFormatter.Format(result.BytesRemoved)} of junk.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
            _history.Record("Junk cleanup",
                $"Removed {ByteFormatter.Format(result.BytesRemoved)} of junk files",
                result.BytesRemoved, result.FilesRemoved, "Broom24");
            _scanResult = null;

            foreach (var category in Categories)
            {
                category.SizeText = "";
                category.FileCount = 0;
            }
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }
}
