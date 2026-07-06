using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class LargeFileItemViewModel : ObservableObject
{
    public LargeFileInfo Info { get; }
    public LargeFileItemViewModel(LargeFileInfo info) => Info = info;

    public string Name => Info.Name;
    public string Directory => Info.Directory;
    public string SizeText => ByteFormatter.Format(Info.SizeBytes);
    public string LastAccessText => Info.LastAccessUtc == default
        ? "—" : Info.LastAccessUtc.ToLocalTime().ToString("d");
    [ObservableProperty] private bool _isSelected;
}

public partial class LargeFilesViewModel : ObservableObject
{
    private readonly ILargeFileScanService _scan;
    private readonly IFileOperationService _files;
    private readonly IHistoryService _history;

    public ObservableCollection<LargeFileItemViewModel> Files { get; } = [];

    /// <summary>Minimum size presets in MB shown in the dropdown.</summary>
    public int[] MinSizeOptions { get; } = [10, 25, 50, 100, 250, 500, 1000];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _selectedFolder;
    [ObservableProperty] private int _minSizeMb = 100;
    [ObservableProperty] private string _statusText = "Pick a folder and scan for the biggest files.";

    public LargeFilesViewModel(ILargeFileScanService scan, IFileOperationService files, IHistoryService history)
    {
        _scan = scan;
        _files = files;
        _history = history;
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to scan for large files" };
        if (dialog.ShowDialog() == true) SelectedFolder = dialog.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanScan), IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder)) { StatusText = "Choose a folder first."; return; }
        IsBusy = true;
        Files.Clear();
        StatusText = $"Scanning {SelectedFolder}…";
        try
        {
            long minBytes = (long)MinSizeMb * 1024 * 1024;
            var results = await _scan.ScanAsync(SelectedFolder!, minBytes, ct);
            foreach (var r in results) Files.Add(new LargeFileItemViewModel(r));
            long total = results.Sum(r => r.SizeBytes);
            StatusText = results.Count == 0
                ? $"No files at or above {MinSizeMb} MB under this folder."
                : $"{results.Count} file(s) ≥ {MinSizeMb} MB — {ByteFormatter.Format(total)} total.";
        }
        catch (OperationCanceledException) { StatusText = "Scan cancelled."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        var picks = Files.Where(f => f.IsSelected).ToList();
        if (picks.Count == 0) { StatusText = "Tick at least one file to delete."; return; }

        IsBusy = true;
        try
        {
            int deleted = 0;
            long freed = 0;
            await Task.Run(() =>
            {
                foreach (var f in picks)
                {
                    if (_files.SendToRecycleBin(f.Info.Path)) { deleted++; freed += f.Info.SizeBytes; }
                }
            });

            foreach (var f in picks.Where(p => !File.Exists(p.Info.Path)).ToList()) Files.Remove(f);
            StatusText = $"Sent {deleted} file(s) to the Recycle Bin — freed {ByteFormatter.Format(freed)}.";
            if (deleted > 0) _history.Record("Large files", StatusText, freed, deleted, "Delete24");
        }
        finally { IsBusy = false; }
    }

    private bool CanScan() => !IsBusy;
    private bool CanDelete() => !IsBusy;
}
