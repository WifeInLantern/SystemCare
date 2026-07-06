using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class ContextMenuItemViewModel : ObservableObject
{
    public ContextMenuEntry Entry { get; private set; }
    public ContextMenuItemViewModel(ContextMenuEntry entry)
    {
        Entry = entry;
        _enabled = entry.Enabled;
    }

    public string Name => Entry.Name;
    public string Location => Entry.Location;
    [ObservableProperty] private bool _enabled;
    public string ToggleLabel => Enabled ? "Disable" : "Enable";

    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleLabel));
        Entry = Entry with { Enabled = value };
    }
}

public partial class ContextMenuViewModel : ObservableObject
{
    private readonly IContextMenuManagerService _menu;

    public ObservableCollection<ContextMenuItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    public ContextMenuViewModel(IContextMenuManagerService menu) => _menu = menu;

    public async void OnNavigatedTo()
    {
        if (Items.Count == 0) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Reading right-click menu entries…";
        try
        {
            var found = await _menu.ListAsync(CancellationToken.None);
            Items.Clear();
            foreach (var e in found) Items.Add(new ContextMenuItemViewModel(e));
            StatusText = $"{found.Count} context-menu handler(s). Disabling one declutters your right-click menu — it's fully reversible.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleAsync(ContextMenuItemViewModel? item)
    {
        if (item is null) return;
        bool target = !item.Enabled;
        var (ok, message) = await _menu.SetEnabledAsync(item.Entry, target);
        if (ok) item.Enabled = target;
        StatusText = message;
    }

    private bool NotBusy() => !IsBusy;
}
