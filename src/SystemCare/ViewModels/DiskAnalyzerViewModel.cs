using System.Collections.ObjectModel;
using System.Windows.Media;
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

/// <summary>
/// One row in the TreeSize-style hierarchy. Children are wrapped lazily — only when the row is
/// expanded — so a whole-drive scan (hundreds of thousands of nodes) doesn't allocate a view-model
/// per file up front. Size is absolute; the bar and percentage are relative to the parent folder.
/// </summary>
public partial class DiskNodeViewModel : ObservableObject
{
    private readonly FileSystemNode? _node;
    private readonly DiskAnalyzerViewModel? _owner;
    private bool _loaded;

    public DiskNodeViewModel? ParentVm { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<DiskNodeViewModel> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    // Loading placeholder so a collapsed directory still shows an expander arrow.
    private DiskNodeViewModel() => IsPlaceholder = true;

    public DiskNodeViewModel(FileSystemNode node, DiskAnalyzerViewModel owner, DiskNodeViewModel? parent = null)
    {
        _node = node;
        _owner = owner;
        ParentVm = parent;
        if (node.IsDirectory && node.Children.Count > 0)
            Children.Add(new DiskNodeViewModel()); // placeholder, replaced on first expand
    }

    public FileSystemNode? Node => _node;
    public string Name => _node?.Name ?? "";
    public string FullPath => _node?.FullPath ?? "";
    public bool IsDirectory => _node?.IsDirectory ?? false;
    public long Size => _node?.Size ?? 0;
    public string SizeText => ByteFormatter.Format(Size);

    /// <summary>Share of the parent folder (the root row is 100%).</summary>
    public double Percent =>
        _node?.Parent is { Size: > 0 } p ? Math.Min(100, _node.Size * 100.0 / p.Size) : 100;

    public string PercentText => Percent.ToString("0.0") + "%";
    public string Icon => IconFor(_node);
    public Brush BarBrush => BrushFor(_node);

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _loaded || _node is null) return;
        _loaded = true;
        Children.Clear();
        foreach (var child in _node.Children) // already sorted largest-first by the scanner
            Children.Add(new DiskNodeViewModel(child, _owner!, this));
    }

    [RelayCommand]
    private void Open()
    {
        if (_owner is not null && _node is not null) _owner.OpenInExplorer(_node.FullPath);
    }

    [RelayCommand]
    private Task Delete() =>
        _owner is null || _node is null ? Task.CompletedTask : _owner.DeleteNodeAsync(this);

    /// <summary>Removes this row from the tree after its target was recycled.</summary>
    public void DetachFromParent() => ParentVm?.Children.Remove(this);

    // ---- presentation helpers ----

    // Only symbol names already proven valid by existing in-app usage. The bar colour carries the
    // finer file-type distinction, so the icon stays coarse on purpose.
    private static string IconFor(FileSystemNode? n)
    {
        if (n is null) return "Document24";
        if (n.IsDirectory) return "FolderOpen24";
        return Path.GetExtension(n.Name).ToLowerInvariant() switch
        {
            ".exe" or ".dll" or ".sys" or ".msi" or ".bin" => "Apps24",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"
                or ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "VideoClip24",
            _ => "Document24",
        };
    }

    private static readonly Brush FolderBrush = Frozen(0x4F, 0x9C, 0xFF);
    private static readonly Brush BinaryBrush = Frozen(0x7A, 0x5C, 0xFF);
    private static readonly Brush MediaBrush = Frozen(0xFF, 0x2A, 0x6D);
    private static readonly Brush ImageBrush = Frozen(0x00, 0xE5, 0xFF);
    private static readonly Brush DocBrush = Frozen(0x00, 0xFF, 0xA3);
    private static readonly Brush ArchiveBrush = Frozen(0xFF, 0xD3, 0x00);
    private static readonly Brush OtherBrush = Frozen(0x8F, 0xA6, 0xC0);

    private static Brush BrushFor(FileSystemNode? n)
    {
        if (n is null) return OtherBrush;
        if (n.IsDirectory) return FolderBrush;
        return Path.GetExtension(n.Name).ToLowerInvariant() switch
        {
            ".exe" or ".dll" or ".sys" or ".msi" or ".bin" => BinaryBrush,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => MediaBrush,
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".raw" => ImageBrush,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" => DocBrush,
            ".zip" or ".rar" or ".7z" or ".iso" or ".cab" or ".gz" or ".vhdx" => ArchiveBrush,
            _ => OtherBrush,
        };
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public partial class DiskAnalyzerViewModel : ObservableObject
{
    private readonly IDiskAnalyzerService _analyzer;
    private readonly IFileOperationService _fileOps;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;

    public ObservableCollection<string> ScanTargets { get; } = [];
    public ObservableCollection<DiskNodeViewModel> TreeRoots { get; } = [];
    public ObservableCollection<LargeFileViewModel> LargeFiles { get; } = [];

    [ObservableProperty] private string _selectedTarget = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
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
        TreeRoots.Clear();
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

            var root = new DiskNodeViewModel(result.Root, this) { IsExpanded = true }; // open the top level
            TreeRoots.Add(root);
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

    public void OpenInExplorer(string path) => _fileOps.OpenInExplorer(path);

    /// <summary>Recycles the file or folder behind a tree row (with confirmation) and removes its row.</summary>
    public async Task DeleteNodeAsync(DiskNodeViewModel item)
    {
        if (item.Node is null) return;
        bool dir = item.IsDirectory;

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = dir ? "Move folder to Recycle Bin?" : "Move to Recycle Bin?",
            Content = $"{item.FullPath}\n({item.SizeText})" +
                      (dir ? "\n\nThe whole folder and everything in it will be moved to the Recycle Bin." : ""),
            PrimaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        bool ok = await Task.Run(() => _fileOps.SendToRecycleBin(item.FullPath));
        if (ok)
        {
            item.DetachFromParent();
            _snackbar.Show("Recycled", $"\"{item.Name}\" was moved to the Recycle Bin.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
        }
        else
        {
            _snackbar.Show("Delete failed", $"\"{item.Name}\" is in use or could not be removed.",
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
        }
    }

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
