using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="SoftwareUpdateViewModel"/> orchestrates <see cref="ISoftwareUpdateService"/> into the UI-bound
/// <c>Updates</c> collection, applies the user's exclusion list, and manages the ignore/reset commands. These
/// tests mock the service so the check/exclusion logic is verified without winget. The apply flow is gated by
/// a WPF-UI confirmation dialog (not unit-mockable); its core is covered by <see cref="SoftwareUpdateServiceTests"/>.
/// </summary>
public class SoftwareUpdateViewModelTests
{
    private static (SoftwareUpdateViewModel Vm, ISoftwareUpdateService Software, ISettingsService Settings, ISnackbarService Snackbar)
        Build(AppSettings? settings = null)
    {
        var software = Substitute.For<ISoftwareUpdateService>();
        software.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        software.GetUpgradesAsync(Arg.Any<CancellationToken>()).Returns(new List<SoftwareUpdate>());

        var settingsSvc = Substitute.For<ISettingsService>();
        settingsSvc.Current.Returns(settings ?? new AppSettings());

        var snackbar = Substitute.For<ISnackbarService>();

        var vm = new SoftwareUpdateViewModel(software, Substitute.For<IRestorePointService>(), settingsSvc,
            snackbar, Substitute.For<IContentDialogService>(), Substitute.For<IHistoryService>(),
            Substitute.For<ILogService>(), Substitute.For<IBackupConfirmationService>());
        return (vm, software, settingsSvc, snackbar);
    }

    private static SoftwareUpdate Update(string id) => new()
    {
        Name = id, Id = id, CurrentVersion = "1.0", AvailableVersion = "2.0", Source = "winget",
    };

    [Fact]
    public async Task Check_WingetAvailable_PopulatesUpdates()
    {
        var (vm, software, _, _) = Build();
        software.GetUpgradesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareUpdate> { Update("A.A"), Update("B.B") });

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.False(vm.IsWingetMissing);
        Assert.True(vm.HasChecked);
        Assert.Equal(2, vm.Updates.Count);
        Assert.Contains("2 app update(s) available", vm.UpdateSummary);
    }

    [Fact]
    public async Task Check_WingetMissing_SetsFlag_AndShowsNoUpdates()
    {
        var (vm, software, _, _) = Build();
        software.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.True(vm.IsWingetMissing);
        Assert.Empty(vm.Updates);
    }

    [Fact]
    public async Task Check_ExcludedIds_AreFilteredOut_AndCounted()
    {
        var (vm, software, _, _) = Build(new AppSettings { SoftwareUpdateExclusions = { "B.B" } });
        software.GetUpgradesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareUpdate> { Update("A.A"), Update("B.B") });

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Single(vm.Updates);
        Assert.Equal("A.A", vm.Updates[0].Id);
        Assert.Equal(1, vm.ExcludedCount);
    }

    [Fact]
    public async Task UpdateSelected_NothingTicked_ShowsSnackbar_DoesNotCallService()
    {
        var (vm, software, _, snackbar) = Build();
        software.GetUpgradesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareUpdate> { Update("A.A") });
        await vm.CheckCommand.ExecuteAsync(null);
        vm.Updates[0].IsSelected = false;

        await vm.UpdateSelectedCommand.ExecuteAsync(null);

        snackbar.ReceivedWithAnyArgs(1).Show(default!, default!, default, default, default);
        await software.DidNotReceiveWithAnyArgs().UpgradeAsync(default!, default!, default);
    }

    [Fact]
    public async Task Exclude_PersistsId_AndRemovesFromList()
    {
        var appSettings = new AppSettings();
        var (vm, software, settings, _) = Build(appSettings);
        software.GetUpgradesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareUpdate> { Update("A.A"), Update("B.B") });
        await vm.CheckCommand.ExecuteAsync(null);

        vm.ExcludeCommand.Execute(vm.Updates.Single(u => u.Id == "A.A"));

        Assert.Contains("A.A", appSettings.SoftwareUpdateExclusions);
        settings.Received().Save();
        Assert.DoesNotContain(vm.Updates, u => u.Id == "A.A");
        Assert.Equal(1, vm.ExcludedCount);
    }

    [Fact]
    public async Task ResetExclusions_ClearsSettings_AndRechecks()
    {
        var appSettings = new AppSettings { SoftwareUpdateExclusions = { "A.A" } };
        var (vm, software, settings, _) = Build(appSettings);

        await vm.ResetExclusionsCommand.ExecuteAsync(null);

        Assert.Empty(appSettings.SoftwareUpdateExclusions);
        settings.Received().Save();
        await software.Received().IsAvailableAsync(Arg.Any<CancellationToken>()); // re-check ran
    }
}
