using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class EmptyFolderItemViewModel(string path) : ObservableObject
{
    public string Path { get; } = path;
    [ObservableProperty] private bool _isSelected = true;
}

public partial class EmptyFolderViewModel : ObservableObject
{
    private readonly IEmptyFolderService _service;
    private readonly IFileOperationService _fileOps;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;

    public ObservableCollection<EmptyFolderItemViewModel> Folders { get; } = [];

    [ObservableProperty] private string _selectedPath = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RemoveCommand))] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _statusText = "Pick a folder and scan to find empty subfolders.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RemoveCommand))] private bool _hasResults;

    private bool CanRemove() => HasResults && !IsBusy;

    public EmptyFolderViewModel(IEmptyFolderService service, IFileOperationService fileOps,
        ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _service = service;
        _fileOps = fileOps;
        _snackbar = snackbar;
        _dialogs = dialogs;
        SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to scan for empty subfolders" };
        if (dialog.ShowDialog() == true) SelectedPath = dialog.FolderName;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SelectedPath) || !Directory.Exists(SelectedPath))
        {
            StatusText = "Choose a valid folder first.";
            return;
        }

        IsBusy = true;
        HasResults = false;
        Folders.Clear();
        var progress = new Progress<string>(p => ProgressText = p);
        try
        {
            var found = await _service.ScanAsync(SelectedPath, progress, ct);
            foreach (var f in found.OrderBy(f => f)) Folders.Add(new EmptyFolderItemViewModel(f));
            HasResults = Folders.Count > 0;
            StatusText = Folders.Count == 0
                ? "No empty folders found."
                : $"Found {Folders.Count} empty folder(s). Selected ones go to the Recycle Bin (recoverable).";
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

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        var selected = Folders.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Remove empty folders?",
            Content = $"{selected.Count} empty folder(s) will be moved to the Recycle Bin (you can restore them from there).",
            PrimaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsBusy = true;
        try
        {
            int removed = 0, failed = 0;
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    if (_fileOps.SendToRecycleBin(item.Path)) removed++;
                    else failed++;
                }
            });
            foreach (var item in selected.Where(i => !Directory.Exists(i.Path)).ToList())
                Folders.Remove(item);
            HasResults = Folders.Count > 0;
            StatusText = $"Removed {removed} folder(s)." + (failed > 0 ? $" {failed} could not be removed." : "");
            _snackbar.Show("Empty folders removed", StatusText, ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
