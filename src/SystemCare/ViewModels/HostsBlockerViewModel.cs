using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class HostsBlockerViewModel : ObservableObject
{
    private readonly IHostsBlockerService _hosts;
    private readonly IHistoryService _history;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseBuiltInCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _isApplied;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _sourceText = "";

    public HostsBlockerViewModel(IHostsBlockerService hosts, IHistoryService history)
    {
        _hosts = hosts;
        _history = history;
    }

    public void OnNavigatedTo() => Refresh();

    private void Refresh()
    {
        var s = _hosts.GetStatus();
        IsApplied = s.IsApplied;
        StatusText = s.IsApplied
            ? $"Active — blocking {s.BlockedCount:N0} ad/tracker domains."
            : "Not active. Applying will block a curated list of ad/tracker domains system-wide.";
        SourceText = _hosts.UsingFetchedList
            ? "Source: StevenBlack community list (downloaded)."
            : "Source: built-in curated list. You can switch to the larger StevenBlack community list below.";
    }

    [RelayCommand(CanExecute = nameof(CanRefreshSource))]
    private async Task RefreshSourceAsync()
    {
        IsBusy = true;
        try
        {
            var (ok, message) = await _hosts.RefreshFromSourceAsync();
            StatusText = message;
            if (ok) _history.Record("Hosts blocker", "Updated community blocklist (StevenBlack)", icon: "ArrowSync24");
            Refresh();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshSource))]
    private async Task UseBuiltInAsync()
    {
        IsBusy = true;
        try
        {
            var (_, message) = await _hosts.UseBuiltInListAsync();
            StatusText = message;
            Refresh();
        }
        finally { IsBusy = false; }
    }

    private bool CanRefreshSource() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        try
        {
            var (ok, message) = await _hosts.ApplyAsync();
            StatusText = message;
            if (ok) _history.Record("Hosts blocker", "Applied ad/tracker blocklist", icon: "ShieldCheckmark24");
            Refresh();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        IsBusy = true;
        try
        {
            var (ok, message) = await _hosts.RemoveAsync();
            StatusText = message;
            if (ok) _history.Record("Hosts blocker", "Removed ad/tracker blocklist", icon: "ArrowUndo24");
            Refresh();
        }
        finally { IsBusy = false; }
    }

    private bool CanApply() => !IsBusy && !IsApplied;
    private bool CanRemove() => !IsBusy && IsApplied;
}
