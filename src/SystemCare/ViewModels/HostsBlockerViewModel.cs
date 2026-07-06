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
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _isApplied;
    [ObservableProperty] private string _statusText = "";

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
            ? $"Active — blocking {s.BlockedCount} ad/tracker domains."
            : "Not active. Applying will block a curated list of ad/tracker domains system-wide.";
    }

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
