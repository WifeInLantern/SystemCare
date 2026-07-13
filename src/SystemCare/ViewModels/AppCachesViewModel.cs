using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class AppCacheItemViewModel : ObservableObject
{
    public AppCacheTarget Target { get; }
    public AppCacheItemViewModel(AppCacheTarget target) => Target = target;

    public string Name => Target.Name;
    public string Group => Target.Group;
    public string Description => Target.Description;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private long _bytes;
    [ObservableProperty] private int _files;

    public string SizeText => Bytes > 0 ? ByteFormatter.Format(Bytes) : "—";

    partial void OnBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));
}

public partial class AppCachesViewModel : ObservableObject
{
    private readonly IAppCacheService _caches;

    public ObservableCollection<AppCacheItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "Scan to measure the app and developer caches on this PC.";

    public AppCachesViewModel(IAppCacheService caches) => _caches = caches;

    public void OnNavigatedTo()
    {
        if (Items.Count > 0) return;
        foreach (var target in _caches.GetAvailableTargets())
            Items.Add(new AppCacheItemViewModel(target));
        if (Items.Count == 0)
            StatusText = "No known app or developer caches were found on this PC.";
    }

    private bool CanScan() => !IsBusy && Items.Count > 0;

    [RelayCommand(CanExecute = nameof(CanScan), IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Measuring caches…";
        try
        {
            var results = await _caches.ScanAsync(Items.Select(i => i.Target), ct);
            long total = 0;
            foreach (var result in results)
            {
                var item = Items.FirstOrDefault(i => i.Target.Id == result.Target.Id);
                if (item is null) continue;
                item.Bytes = result.Bytes;
                item.Files = result.Files;
                item.IsSelected = result.Bytes > 0;
                total += result.Bytes;
            }
            StatusText = total > 0
                ? $"{ByteFormatter.Format(total)} of regenerable cache found. Untick anything you want to keep."
                : "Caches are already tidy — nothing significant to clean.";
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

    // Selection isn't part of CanExecute (toolkit commands don't re-query on item changes);
    // cleaning with nothing selected is a harmless no-op with a clear status message.
    private bool CanClean() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClean), IncludeCancelCommand = true)]
    private async Task CleanSelectedAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Cleaning selected caches…";
        try
        {
            var selected = Items.Where(i => i.IsSelected).Select(i => i.Target).ToList();
            var result = await _caches.CleanAsync(selected, ct);
            StatusText = result.FilesRemoved > 0
                ? $"Freed {ByteFormatter.Format(result.BytesRemoved)} ({result.FilesRemoved:N0} files). " +
                  (result.FilesSkipped > 0 ? $"{result.FilesSkipped:N0} in-use or recent files were left alone." : "")
                : "Nothing removed — files were in use or written too recently (24h protection).";

            // Refresh sizes so the list reflects reality after the clean.
            var rescans = await _caches.ScanAsync(selected, ct);
            foreach (var rescan in rescans)
            {
                var item = Items.FirstOrDefault(i => i.Target.Id == rescan.Target.Id);
                if (item is null) continue;
                item.Bytes = rescan.Bytes;
                item.Files = rescan.Files;
                item.IsSelected = false;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Clean cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
