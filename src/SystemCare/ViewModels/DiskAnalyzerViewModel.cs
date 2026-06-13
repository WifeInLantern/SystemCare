using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class LargeFileViewModel(LargeFileEntry entry, DiskAnalyzerViewModel owner) : ObservableObject
{
    public LargeFileEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Directory => Entry.Directory;
    public string SizeText => ByteFormatter.Format(Entry.Size);

    [RelayCommand] private void Open() => owner.OpenInExplorer(Entry.FullPath);
    [RelayCommand] private Task Delete() => owner.DeleteLargeFileAsync(this);
}

public partial class DiskAnalyzerViewModel : ObservableObject
{
    private readonly IDiskAnalyzerService _analyzer;
    private readonly IFileOperationService _fileOps;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;

    public ObservableCollection<string> ScanTargets { get; } = [];
    public ObservableCollection<FileSystemNode> Breadcrumbs { get; } = [];
    public ObservableCollection<LargeFileViewModel> LargeFiles { get; } = [];

    [ObservableProperty] private string _selectedTarget = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _hoverText = "";
    [ObservableProperty] private FileSystemNode? _currentRoot;
    [ObservableProperty] private bool _hasResult;

    public DiskAnalyzerViewModel(
        IDiskAnalyzerService analyzer,
        IFileOperationService fileOps,
        ISettingsService settings,
        ISnackbarService snackbar,
        IContentDialogService dialogs)
    {
        _analyzer = analyzer;
        _fileOps = fileOps;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    ScanTargets.Add(drive.Name);
            }
            catch (Exception) { }
        }
        SelectedTarget = ScanTargets.FirstOrDefault() ?? "C:\\";
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to analyze" };
        if (dialog.ShowDialog() == true)
        {
            if (!ScanTargets.Contains(dialog.FolderName))
                ScanTargets.Add(dialog.FolderName);
            SelectedTarget = dialog.FolderName;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SelectedTarget)) return;
        IsBusy = true;
        HasResult = false;
        CurrentRoot = null;
        Breadcrumbs.Clear();
        LargeFiles.Clear();

        var progress = new Progress<DiskScanProgress>(p =>
            ProgressText = $"{p.FilesSeen:N0} files · {ByteFormatter.Format(p.BytesSeen)} — {p.CurrentPath}");

        try
        {
            var result = await _analyzer.ScanAsync(
                SelectedTarget,
                _settings.Current.LargeFileTopN,
                _settings.Current.LargeFileMinMB * 1024L * 1024L,
                progress, ct);

            CurrentRoot = result.Root;
            Breadcrumbs.Add(result.Root);
            foreach (var file in result.LargeFiles)
                LargeFiles.Add(new LargeFileViewModel(file, this));

            HasResult = true;
            ProgressText = $"Scanned {result.FilesSeen:N0} files · {ByteFormatter.Format(result.Root.Size)} total" +
                           (result.InaccessibleEntries > 0 ? $" · {result.InaccessibleEntries:N0} entries not accessible" : "");
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Scan cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void DrillTo(FileSystemNode node)
    {
        if (!node.IsDirectory) return;
        CurrentRoot = node;

        Breadcrumbs.Clear();
        var chain = new List<FileSystemNode>();
        for (var current = node; current is not null; current = current.Parent)
            chain.Add(current);
        chain.Reverse();
        foreach (var link in chain) Breadcrumbs.Add(link);
    }

    [RelayCommand]
    private void NavigateBreadcrumb(FileSystemNode node) => DrillTo(node);

    public void OpenInExplorer(string path) => _fileOps.OpenInExplorer(path);

    public async Task DeleteLargeFileAsync(LargeFileViewModel item)
    {
        var result = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Move to Recycle Bin?",
            Content = $"{item.Entry.FullPath}\n({item.SizeText})",
            PrimaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
        });
        if (result != ContentDialogResult.Primary) return;

        if (_fileOps.SendToRecycleBin(item.Entry.FullPath))
        {
            LargeFiles.Remove(item);
            _snackbar.Show("File recycled", $"\"{item.Name}\" was moved to the Recycle Bin.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
        }
        else
        {
            _snackbar.Show("Delete failed", $"\"{item.Name}\" is in use or no longer exists.",
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
        }
    }
}
