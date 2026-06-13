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

public partial class DuplicateFileViewModel(DuplicateFile file, DuplicateGroupViewModel group) : ObservableObject
{
    public DuplicateFile File { get; } = file;
    public string FullPath => File.FullPath;
    public string ModifiedText => File.ModifiedUtc.ToLocalTime().ToString("g");

    internal bool SuppressGuard;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (!SuppressGuard) group.OnSelectionChanged(this, value);
    }
}

public partial class DuplicateGroupViewModel : ObservableObject
{
    private readonly DuplicateFinderViewModel _owner;

    public ObservableCollection<DuplicateFileViewModel> Files { get; } = [];
    public long Size { get; }
    public string Header { get; }

    public DuplicateGroupViewModel(DuplicateGroup group, DuplicateFinderViewModel owner)
    {
        _owner = owner;
        Size = group.Size;
        Header = $"{group.Files.Count} copies · {ByteFormatter.Format(group.Size)} each · {ByteFormatter.Format(group.WastedBytes)} wasted";
        foreach (var file in group.Files)
            Files.Add(new DuplicateFileViewModel(file, this));
    }

    /// <summary>Keep-at-least-one invariant: selecting the final unselected copy is rejected.</summary>
    public void OnSelectionChanged(DuplicateFileViewModel item, bool selected)
    {
        if (selected && Files.All(f => f.IsSelected))
        {
            item.SuppressGuard = true;
            item.IsSelected = false;
            item.SuppressGuard = false;
            _owner.NotifyKeepOneRule();
            return;
        }
        _owner.UpdateSelectionSummary();
    }

    public void SelectAllBut(DuplicateFileViewModel keep)
    {
        foreach (var file in Files)
        {
            file.SuppressGuard = true;
            file.IsSelected = !ReferenceEquals(file, keep);
            file.SuppressGuard = false;
        }
    }
}

public partial class DuplicateFinderViewModel : ObservableObject
{
    private readonly IDuplicateFinderService _finder;
    private readonly IFileOperationService _fileOps;
    private readonly ISettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private DateTime _lastKeepOneNotice = DateTime.MinValue;

    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private string? _selectedRoot;
    [ObservableProperty] private int _minSizeMB;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Add folders to search, then scan.";
    [ObservableProperty] private string _selectionText = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))] private bool _hasSelection;

    public DuplicateFinderViewModel(
        IDuplicateFinderService finder,
        IFileOperationService fileOps,
        ISettingsService settings,
        ISnackbarService snackbar,
        IContentDialogService dialogs)
    {
        _finder = finder;
        _fileOps = fileOps;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _minSizeMB = settings.Current.DupMinSizeMB;

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var sub in new[] { "Downloads", "Documents", "Pictures" })
        {
            string path = Path.Combine(profile, sub);
            if (Directory.Exists(path)) Roots.Add(path);
        }
    }

    partial void OnMinSizeMBChanged(int value)
    {
        _settings.Current.DupMinSizeMB = Math.Max(0, value);
        _settings.Save();
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder to search for duplicates" };
        if (dialog.ShowDialog() == true && !Roots.Contains(dialog.FolderName))
            Roots.Add(dialog.FolderName);
    }

    [RelayCommand]
    private void RemoveRoot()
    {
        if (SelectedRoot is not null) Roots.Remove(SelectedRoot);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        if (Roots.Count == 0)
        {
            StatusText = "Add at least one folder first.";
            return;
        }

        IsBusy = true;
        Groups.Clear();
        HasSelection = false;
        SelectionText = "";

        var progress = new Progress<DuplicateScanProgress>(p => StatusText = p.Stage switch
        {
            DuplicateStage.Enumerating => $"Listing files… {p.Current:N0} seen",
            DuplicateStage.PartialHashing => $"Quick-checking candidates… {p.Current:N0}/{p.Total:N0}",
            DuplicateStage.FullHashing => $"Verifying duplicates… {p.Current:N0}/{p.Total:N0} — {Path.GetFileName(p.CurrentFile)}",
            _ => "",
        });

        try
        {
            var groups = await _finder.FindAsync(Roots, Math.Max(0, MinSizeMB) * 1024L * 1024L, progress, ct);
            foreach (var group in groups)
                Groups.Add(new DuplicateGroupViewModel(group, this));

            long wasted = groups.Sum(g => g.WastedBytes);
            StatusText = groups.Count == 0
                ? "No duplicates found."
                : $"{groups.Count:N0} duplicate groups — {ByteFormatter.Format(wasted)} wasted by extra copies.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void KeepNewest()
    {
        foreach (var group in Groups)
        {
            var newest = group.Files.OrderByDescending(f => f.File.ModifiedUtc).First();
            group.SelectAllBut(newest);
        }
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void KeepOldest()
    {
        foreach (var group in Groups)
        {
            var oldest = group.Files.OrderBy(f => f.File.ModifiedUtc).First();
            group.SelectAllBut(oldest);
        }
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var file in Groups.SelectMany(g => g.Files))
        {
            file.SuppressGuard = true;
            file.IsSelected = false;
            file.SuppressGuard = false;
        }
        UpdateSelectionSummary();
    }

    public void NotifyKeepOneRule()
    {
        if ((DateTime.UtcNow - _lastKeepOneNotice).TotalSeconds < 5) return;
        _lastKeepOneNotice = DateTime.UtcNow;
        _snackbar.Show("One copy must remain",
            "At least one file in each group has to stay unselected.",
            ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
    }

    public void UpdateSelectionSummary()
    {
        var selected = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).ToList();
        HasSelection = selected.Count > 0;
        SelectionText = selected.Count == 0
            ? ""
            : $"{selected.Count:N0} files selected · {ByteFormatter.Format(selected.Sum(f => f.File.Size))} will be reclaimed";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelectedAsync()
    {
        var selected = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        long bytes = selected.Sum(f => f.File.Size);
        var result = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Move duplicates to Recycle Bin?",
            Content = $"{selected.Count:N0} files ({ByteFormatter.Format(bytes)}) will be moved to the Recycle Bin.\n" +
                      "At least one copy of every file stays where it is.",
            PrimaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
        });
        if (result != ContentDialogResult.Primary) return;

        IsBusy = true;
        try
        {
            int deleted = 0, failed = 0;
            await Task.Run(() =>
            {
                foreach (var file in selected)
                {
                    if (_fileOps.SendToRecycleBin(file.FullPath)) deleted++;
                    else failed++;
                }
            });

            foreach (var group in Groups.ToList())
            {
                foreach (var file in group.Files.Where(f => f.IsSelected && !File.Exists(f.FullPath)).ToList())
                    group.Files.Remove(file);
                if (group.Files.Count < 2) Groups.Remove(group);
            }

            UpdateSelectionSummary();
            StatusText = $"Moved {deleted:N0} duplicates to the Recycle Bin." +
                         (failed > 0 ? $" {failed:N0} files could not be moved." : "");
            _snackbar.Show("Duplicates removed", StatusText,
                ControlAppearance.Success, null, TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
