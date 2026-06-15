using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class RegistryCategoryItemViewModel(RegistryCategory category) : ObservableObject
{
    public RegistryCategory Category { get; } = category;
    public string Name => Category.Name;
    public string Description => Category.Description;
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private int _foundCount;
}

public partial class RegistryCleanerViewModel : ObservableObject
{
    private readonly IRegistryCleanerService _registry;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly IHistoryService _history;
    private List<RegistryIssue> _lastScan = [];

    public ObservableCollection<RegistryCategoryItemViewModel> Categories { get; }
    public ObservableCollection<RegistryIssue> Issues { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _statusText = "Scan finds registry entries that point to files or folders that no longer exist.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CleanCommand))] private bool _canClean;

    public RegistryCleanerViewModel(IRegistryCleanerService registry, ISnackbarService snackbar,
        IContentDialogService dialogs, IHistoryService history)
    {
        _registry = registry;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _history = history;
        Categories = new ObservableCollection<RegistryCategoryItemViewModel>(
            registry.Categories.Select(c => new RegistryCategoryItemViewModel(c)));
    }

    private List<string> SelectedCategoryIds() =>
        Categories.Where(c => c.IsSelected).Select(c => c.Category.Id).ToList();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsBusy = true;
        CanClean = false;
        Issues.Clear();
        foreach (var c in Categories) c.FoundCount = 0;

        var progress = new Progress<string>(s => ProgressText = s);
        try
        {
            _lastScan = await _registry.ScanAsync(SelectedCategoryIds(), progress, ct);
            foreach (var issue in _lastScan) Issues.Add(issue);
            foreach (var c in Categories)
                c.FoundCount = _lastScan.Count(i => i.CategoryId == c.Category.Id);

            StatusText = _lastScan.Count == 0
                ? "No invalid registry entries found — your registry looks clean."
                : $"Found {_lastScan.Count} invalid entr{(_lastScan.Count == 1 ? "y" : "ies")}. Review, then clean (a backup is made automatically).";
            CanClean = _lastScan.Count > 0;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
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
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Clean registry entries?",
            Content = $"{Issues.Count} invalid entr{(Issues.Count == 1 ? "y" : "ies")} will be removed.\n\n" +
                      "A backup (.reg) is saved first and a restore point is created — you can undo with " +
                      "\"Restore last backup\".",
            PrimaryButtonText = "Clean now",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsBusy = true;
        CanClean = false;
        var progress = new Progress<string>(s => ProgressText = s);
        try
        {
            var result = await _registry.CleanAsync(_lastScan, progress, CancellationToken.None);
            Issues.Clear();
            foreach (var c in Categories) c.FoundCount = 0;
            StatusText = $"Removed {result.Removed} entr{(result.Removed == 1 ? "y" : "ies")}" +
                         (result.Skipped > 0 ? $" ({result.Skipped} skipped)" : "") + ". Backup saved.";
            _snackbar.Show("Registry cleaned",
                $"Removed {result.Removed} entries. Backup saved to the RegistryBackups folder.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(6));
            if (result.Removed > 0)
                _history.Record("Registry clean",
                    $"Removed {result.Removed} invalid entr{(result.Removed == 1 ? "y" : "ies")} (backup saved)",
                    0, result.Removed, "Broom24");
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private async Task RestoreLastBackupAsync()
    {
        var (ok, message) = await _registry.RestoreLastBackupAsync();
        _snackbar.Show(ok ? "Backup restored" : "Restore",
            message, ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
        StatusText = message;
    }

    [RelayCommand]
    private void OpenBackupsFolder() => _registry.OpenBackupsFolder();
}
