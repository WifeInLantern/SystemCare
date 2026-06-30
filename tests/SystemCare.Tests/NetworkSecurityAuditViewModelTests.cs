using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="NetworkSecurityAuditViewModel"/> lists listening ports and SystemCare-created firewall
/// block rules, gating both block and unblock behind an explicit confirm dialog (not a restore-point
/// gate, since firewall rules aren't part of System Restore's tracked state). These tests mock
/// <see cref="INetworkToolsService"/>, <see cref="IFirewallService"/>, and <see cref="IConfirmDialogService"/>
/// so no real firewall rule or socket table is touched.
/// </summary>
public class NetworkSecurityAuditViewModelTests
{
    private static (NetworkSecurityAuditViewModel Vm, INetworkToolsService Network, IFirewallService Firewall,
        IConfirmDialogService Confirm, IHistoryService History) Build(bool confirm = true)
    {
        var network = Substitute.For<INetworkToolsService>();
        var firewall = Substitute.For<IFirewallService>();
        var confirmDialog = Substitute.For<IConfirmDialogService>();
        confirmDialog.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .ReturnsForAnyArgs(Task.FromResult(confirm));
        var snackbar = Substitute.For<ISnackbarService>();
        var history = Substitute.For<IHistoryService>();

        var vm = new NetworkSecurityAuditViewModel(network, firewall, confirmDialog, snackbar, history);
        return (vm, network, firewall, confirmDialog, history);
    }

    private static ListeningPort Port(string proc = "chrome", string? path = @"C:\chrome.exe") => new()
    {
        Protocol = "TCP",
        LocalAddress = "0.0.0.0",
        Port = 443,
        Pid = 1234,
        ProcessName = proc,
        ProcessPath = path,
    };

    [Fact]
    public async Task RefreshCommand_PopulatesPortsAndBlockedApps()
    {
        var (vm, network, firewall, _, _) = Build();
        network.GetListeningPorts().Returns([Port()]);
        firewall.GetRulesAsync().Returns(Task.FromResult(new List<BlockedApp>
        {
            new() { RuleName = "SystemCare Block - notepad", DisplayName = "notepad", ApplicationPath = @"C:\notepad.exe" },
        }));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(vm.ListeningPorts);
        Assert.Single(vm.BlockedApps);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task BlockAppCommand_WhenConfirmed_CallsFirewallAndRecordsHistory()
    {
        var (vm, network, firewall, _, history) = Build(confirm: true);
        network.GetListeningPorts().Returns([]);
        firewall.BlockApplicationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        firewall.GetRulesAsync().Returns(Task.FromResult(new List<BlockedApp>()));

        await vm.BlockAppCommand.ExecuteAsync(Port());

        await firewall.Received(1).BlockApplicationAsync(@"C:\chrome.exe", "chrome");
        history.Received(1).Record(Arg.Any<string>(), "chrome", Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task BlockAppCommand_WhenCancelled_DoesNotCallFirewall()
    {
        var (vm, _, firewall, _, _) = Build(confirm: false);

        await vm.BlockAppCommand.ExecuteAsync(Port());

        await firewall.DidNotReceive().BlockApplicationAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task BlockAppCommand_WhenProcessPathUnresolved_DoesNotPromptOrCallFirewall()
    {
        var (vm, _, firewall, confirmDialog, _) = Build();

        await vm.BlockAppCommand.ExecuteAsync(Port(path: null));

        await confirmDialog.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await firewall.DidNotReceive().BlockApplicationAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UnblockAppCommand_WhenConfirmed_CallsFirewall()
    {
        var (vm, network, firewall, _, _) = Build(confirm: true);
        network.GetListeningPorts().Returns([]);
        firewall.UnblockApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult(true));
        firewall.GetRulesAsync().Returns(Task.FromResult(new List<BlockedApp>()));
        var app = new BlockedApp { RuleName = "SystemCare Block - notepad", DisplayName = "notepad", ApplicationPath = @"C:\notepad.exe" };

        await vm.UnblockAppCommand.ExecuteAsync(app);

        await firewall.Received(1).UnblockApplicationAsync("SystemCare Block - notepad");
    }

    [Fact]
    public async Task UnblockAppCommand_WhenCancelled_DoesNotCallFirewall()
    {
        var (vm, _, firewall, _, _) = Build(confirm: false);
        var app = new BlockedApp { RuleName = "SystemCare Block - notepad", DisplayName = "notepad", ApplicationPath = @"C:\notepad.exe" };

        await vm.UnblockAppCommand.ExecuteAsync(app);

        await firewall.DidNotReceive().UnblockApplicationAsync(Arg.Any<string>());
    }
}
