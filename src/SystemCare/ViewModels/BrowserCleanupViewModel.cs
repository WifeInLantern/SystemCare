using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class BrowserItemViewModel : ObservableObject
{
    public BrowserInfo Info { get; }
    public BrowserItemViewModel(BrowserInfo info) => Info = info;

    public string Name => Info.Name;
    public string CacheText => $"Cache: {ByteFormatter.Format(Info.CacheBytes)}";
    [ObservableProperty] private bool _isSelected = true;
}

public partial class BrowserCleanupViewModel : ObservableObject
{
    private readonly IBrowserCleanupService _browsers;
    private readonly IHistoryService _history;

    public ObservableCollection<BrowserItemViewModel> Browsers { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _clearCache = true;
    [ObservableProperty] private bool _clearCookies;
    [ObservableProperty] private bool _clearHistory;
    [ObservableProperty] private string _statusText = "";

    public BrowserCleanupViewModel(IBrowserCleanupService browsers, IHistoryService history)
    {
        _browsers = browsers;
        _history = history;
    }

    public async void OnNavigatedTo()
    {
        if (Browsers.Count == 0) await ScanAsync();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Looking for installed browsers…";
        try
        {
            var found = await _browsers.DetectAsync(CancellationToken.None);
            Browsers.Clear();
            foreach (var b in found) Browsers.Add(new BrowserItemViewModel(b));
            StatusText = found.Count == 0
                ? "No supported browsers found."
                : $"Found {found.Count} browser(s). Close them before cleaning so locked files can be removed.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task CleanAsync()
    {
        var picks = Browsers.Where(b => b.IsSelected).ToList();
        if (picks.Count == 0) { StatusText = "Select at least one browser."; return; }
        if (!ClearCache && !ClearCookies && !ClearHistory) { StatusText = "Choose what to clear (cache, cookies, history)."; return; }

        IsBusy = true;
        StatusText = "Cleaning…";
        try
        {
            long freed = 0;
            foreach (var b in picks)
                freed += await _browsers.ClearAsync(b.Info, ClearCache, ClearCookies, ClearHistory, CancellationToken.None);

            StatusText = $"Done — freed {ByteFormatter.Format(freed)}.";
            if (freed > 0) _history.Record("Browser cleanup", StatusText, freed, picks.Count, "Broom24");
            await ScanAsync();
        }
        finally { IsBusy = false; }
    }

    private bool NotBusy() => !IsBusy;
}
