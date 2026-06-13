using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SystemCare.Helpers;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class ShredItemViewModel(string path, bool isFolder, string sizeText)
{
    public string Path { get; } = path;
    public bool IsFolder { get; } = isFolder;
    public string SizeText { get; } = sizeText;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } n ? n : Path;
}

public partial class FileShredderViewModel : ObservableObject
{
    private readonly IFileShredderService _shredder;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ShredItemViewModel> Items { get; } = [];

    [ObservableProperty] private int _passes = 1;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ShredCommand))] private bool _hasItems;

    public FileShredderViewModel(IFileShredderService shredder, ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _shredder = shredder;
        _snackbar = snackbar;
        _dialogs = dialogs;
    }

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Title = "Choose files to shred", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var file in dialog.FileNames)
        {
            if (Items.Any(i => i.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            long size = 0;
            try { size = new FileInfo(file).Length; } catch (Exception) { }
            Items.Add(new ShredItemViewModel(file, false, ByteFormatter.Format(size)));
        }
        HasItems = Items.Count > 0;
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to shred" };
        if (dialog.ShowDialog() != true) return;
        if (Items.Any(i => i.Path.Equals(dialog.FolderName, StringComparison.OrdinalIgnoreCase))) return;
        var (bytes, _) = SafeFileEnumerator.Measure(dialog.FolderName);
        Items.Add(new ShredItemViewModel(dialog.FolderName, true, ByteFormatter.Format(bytes)));
        HasItems = Items.Count > 0;
    }

    [RelayCommand]
    private void Remove(ShredItemViewModel item)
    {
        Items.Remove(item);
        HasItems = Items.Count > 0;
    }

    [RelayCommand]
    private void Clear()
    {
        Items.Clear();
        HasItems = false;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task ShredAsync()
    {
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Permanently shred these items?",
            Content = $"{Items.Count} item(s) will be overwritten {Passes} time(s) and deleted.\n\n" +
                      "This CANNOT be undone — shredded files are not recoverable.",
            PrimaryButtonText = "Shred permanently",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<ShredProgress>(p =>
            ProgressText = $"Shredding {p.FilesDone}/{p.FilesTotal}: {p.CurrentFile}");

        try
        {
            var paths = Items.Select(i => i.Path).ToList();
            var result = await _shredder.ShredAsync(paths, Passes, progress, _cts.Token);
            _snackbar.Show("Shred complete",
                $"Shredded {result.FilesShredded} file(s) ({ByteFormatter.Format(result.BytesShredded)})." +
                (result.FilesSkipped > 0 ? $" {result.FilesSkipped} in-use file(s) skipped." : ""),
                ControlAppearance.Success, null, TimeSpan.FromSeconds(6));
            Items.Clear();
            HasItems = false;
            ProgressText = "";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
