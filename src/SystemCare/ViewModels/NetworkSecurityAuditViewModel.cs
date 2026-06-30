using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class NetworkSecurityAuditViewModel : ObservableObject
{
    private readonly INetworkToolsService _network;
    private readonly IFirewallService _firewall;
    private readonly IContentDialogService _dialogs;
    private readonly ISnackbarService _snackbar;
    private readonly IHistoryService _history;

    public ObservableCollection<ListeningPort> ListeningPorts { get; } = [];
    public ObservableCollection<BlockedApp> BlockedApps { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _summaryText = "";

    public NetworkSecurityAuditViewModel(
        INetworkToolsService network,
        IFirewallService firewall,
        IContentDialogService dialogs,
        ISnackbarService snackbar,
        IHistoryService history)
    {
        _network = network;
        _firewall = firewall;
        _dialogs = dialogs;
        _snackbar = snackbar;
        _history = history;
    }

    public async void OnNavigatedTo()
    {
        if (ListeningPorts.Count == 0 && BlockedApps.Count == 0) await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var ports = await Task.Run(_network.GetListeningPorts);
            ListeningPorts.Clear();
            foreach (var p in ports) ListeningPorts.Add(p);

            var blocked = await _firewall.GetRulesAsync();
            BlockedApps.Clear();
            foreach (var b in blocked) BlockedApps.Add(b);

            SummaryText = $"{ListeningPorts.Count} listening port(s) · {BlockedApps.Count} blocked app(s)";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BlockAppAsync(ListeningPort port)
    {
        if (string.IsNullOrWhiteSpace(port.ProcessPath))
        {
            _snackbar.Show("Can't block this app", $"Couldn't resolve the executable path for {port.ProcessName}.",
                ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
            return;
        }

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = $"Block {port.ProcessName} from the network?",
            Content = $"Adds a Windows Firewall rule blocking all inbound and outbound traffic for " +
                      $"\"{port.ProcessPath}\". You can remove this later from the Blocked apps list.",
            PrimaryButtonText = "Block",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsBusy = true;
        try
        {
            bool ok = await _firewall.BlockApplicationAsync(port.ProcessPath, port.ProcessName);
            if (ok)
            {
                _history.Record("Blocked app firewall access", port.ProcessName, 0, 1, "Shield24");
                _snackbar.Show("Blocked", $"{port.ProcessName} was blocked from the network.",
                    ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
                await RefreshAsync();
            }
            else
            {
                _snackbar.Show("Block failed", $"Could not add a firewall rule for {port.ProcessName}.",
                    ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnblockAppAsync(BlockedApp app)
    {
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = $"Unblock {app.DisplayName}?",
            Content = "This removes the firewall rule SystemCare created, restoring normal network access.",
            PrimaryButtonText = "Unblock",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsBusy = true;
        try
        {
            bool ok = await _firewall.UnblockApplicationAsync(app.RuleName);
            if (ok)
            {
                _snackbar.Show("Unblocked", $"{app.DisplayName} can access the network again.",
                    ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
                await RefreshAsync();
            }
            else
            {
                _snackbar.Show("Unblock failed", $"Could not remove the firewall rule for {app.DisplayName}.",
                    ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
