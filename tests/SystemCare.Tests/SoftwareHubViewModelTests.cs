using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="SoftwareHubViewModel"/> orchestrates <see cref="ISoftwareHubService"/> into the UI-bound
/// <c>Apps</c>/<c>GroupedApps</c> collections and gates installs behind a "nothing selected" guard. These
/// tests mock the service so refresh/selection logic is verified without winget. The full install flow is
/// gated by a WPF-UI confirmation dialog shown via an extension method (not unit-mockable, same limitation
/// as <c>SoftwareUpdateViewModelTests</c>); that flow's core (progress, counting, history) is covered by
/// <see cref="SoftwareHubServiceTests"/> instead.
/// </summary>
public class SoftwareHubViewModelTests
{
    private static (SoftwareHubViewModel Vm, ISoftwareHubService Software, ISnackbarService Snackbar) Build()
    {
        var software = Substitute.For<ISoftwareHubService>();
        software.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        software.GetCatalogAsync(Arg.Any<CancellationToken>()).Returns(new List<SoftwareHubAppStatus>());

        var snackbar = Substitute.For<ISnackbarService>();

        var vm = new SoftwareHubViewModel(software, Substitute.For<IRestorePointService>(),
            snackbar, Substitute.For<IContentDialogService>(), Substitute.For<IHistoryService>(),
            Substitute.For<ILogService>(), Substitute.For<IBackupConfirmationService>());
        return (vm, software, snackbar);
    }

    private static SoftwareHubAppStatus Status(string id, string category, bool installed = false) => new()
    {
        App = new SoftwareHubApp { Name = id, Id = id, Category = category, Description = "" },
        IsInstalled = installed,
    };

    [Fact]
    public async Task Refresh_WingetAvailable_PopulatesApps_GroupedByCategory()
    {
        var (vm, software, _) = Build();
        software.GetCatalogAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareHubAppStatus> { Status("A.A", "Browsers"), Status("B.B", "Utilities") });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.IsWingetMissing);
        Assert.True(vm.HasChecked);
        Assert.Equal(2, vm.Apps.Count);
        Assert.Equal(2, vm.GroupedApps.Groups!.Count);
    }

    [Fact]
    public async Task Refresh_WingetMissing_SetsFlag_AndShowsNoApps()
    {
        var (vm, software, _) = Build();
        software.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.IsWingetMissing);
        Assert.Empty(vm.Apps);
    }

    [Fact]
    public async Task InstallSelected_NothingTicked_ShowsSnackbar_DoesNotCallService()
    {
        var (vm, software, snackbar) = Build();
        software.GetCatalogAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareHubAppStatus> { Status("A.A", "Utilities") });
        await vm.RefreshCommand.ExecuteAsync(null);
        // IsSelected already defaults to false — nothing to un-tick.

        await vm.InstallSelectedCommand.ExecuteAsync(null);

        snackbar.ReceivedWithAnyArgs(1).Show(default!, default!, default, default, default);
        await software.DidNotReceiveWithAnyArgs().InstallAsync(default!, default, default);
    }

    [Fact]
    public async Task InstallSelected_OnlyAlreadyInstalledAppsTicked_TreatedAsNothingSelected()
    {
        var (vm, software, snackbar) = Build();
        software.GetCatalogAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareHubAppStatus> { Status("A.A", "Utilities", installed: true) });
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Apps[0].IsSelected = true; // simulate a stale/forced selection on an installed item

        await vm.InstallSelectedCommand.ExecuteAsync(null);

        snackbar.ReceivedWithAnyArgs(1).Show(default!, default!, default, default, default);
        await software.DidNotReceiveWithAnyArgs().InstallAsync(default!, default, default);
    }
}
