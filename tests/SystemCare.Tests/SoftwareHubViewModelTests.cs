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

    [Fact]
    public async Task SearchText_PopulatesSearchResults_AndHidesCatalog()
    {
        var (vm, software, _) = Build();
        vm.SearchDebounceMs = 0;
        software.GetCatalogAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareHubAppStatus> { Status("A.A", "Utilities") });
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.True(vm.ShowCatalog);

        software.SearchAsync("vlc", Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareHubAppStatus> { Status("VideoLAN.VLC", "Search results", installed: true) });

        vm.SearchText = "vlc";
        await vm.ActiveSearchTask!;

        Assert.True(vm.IsSearchMode);
        Assert.False(vm.ShowCatalog);
        Assert.False(vm.IsSearching);
        var row = Assert.Single(vm.SearchResults);
        Assert.Equal("VideoLAN.VLC", row.Id);
        Assert.True(row.IsInstalled);
        Assert.Contains("1 result", vm.SearchStatusText);
    }

    [Fact]
    public async Task ClearingSearchText_RestoresCatalogWithoutAnotherServiceCall()
    {
        var (vm, software, _) = Build();
        vm.SearchDebounceMs = 0;
        software.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareHubAppStatus> { Status("VideoLAN.VLC", "Search results") });
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.SearchText = "vlc";
        await vm.ActiveSearchTask!;
        Assert.True(vm.IsSearchMode);

        software.ClearReceivedCalls();
        vm.SearchText = "";

        Assert.False(vm.IsSearchMode);
        Assert.True(vm.ShowCatalog);
        Assert.Empty(vm.SearchResults);
        await software.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
        await software.DidNotReceiveWithAnyArgs().GetCatalogAsync(default);
    }

    [Fact]
    public async Task NewerSearch_SupersedesOlderInFlightSearch()
    {
        var (vm, software, _) = Build();
        vm.SearchDebounceMs = 0;
        var older = new TaskCompletionSource<List<SoftwareHubAppStatus>>();
        var newer = new TaskCompletionSource<List<SoftwareHubAppStatus>>();
        software.SearchAsync("v", Arg.Any<CancellationToken>()).Returns(_ => older.Task);
        software.SearchAsync("vlc", Arg.Any<CancellationToken>()).Returns(_ => newer.Task);

        vm.SearchText = "v";
        var olderTask = vm.ActiveSearchTask!;
        vm.SearchText = "vlc";
        var newerTask = vm.ActiveSearchTask!;

        newer.SetResult([Status("VideoLAN.VLC", "Search results")]);
        await newerTask;
        // The older search completes late — its results must not clobber the newer ones.
        older.SetResult([Status("Stale.App", "Search results")]);
        await olderTask;

        var row = Assert.Single(vm.SearchResults);
        Assert.Equal("VideoLAN.VLC", row.Id);
    }

    [Fact]
    public async Task SearchServiceFailure_ShowsFailureStatus_InsteadOfCrashing()
    {
        var (vm, software, _) = Build();
        vm.SearchDebounceMs = 0;
        software.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<List<SoftwareHubAppStatus>>>(_ => throw new InvalidOperationException("boom"));

        vm.SearchText = "vlc";
        await vm.ActiveSearchTask!;

        Assert.True(vm.IsSearchMode);
        Assert.False(vm.IsSearching);
        Assert.Contains("failed", vm.SearchStatusText, StringComparison.OrdinalIgnoreCase);
    }
}
