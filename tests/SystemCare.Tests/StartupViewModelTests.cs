using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="StartupViewModel"/> filters its loaded entries by enabled/disabled state and free-text
/// search, and reports whether a row's enable/disable toggle actually took effect. These tests mock
/// <see cref="IStartupManagerService"/> so the filtering/toggle logic can be verified without touching
/// the real registry/Task Scheduler.
/// </summary>
public class StartupViewModelTests
{
    private static StartupEntry Entry(string name, string publisher, bool enabled, string command = "") => new()
    {
        Name = name,
        Publisher = publisher,
        Command = command,
        IsEnabled = enabled,
        Source = StartupSource.HkcuRun,
        RawKey = name,
    };

    private static (StartupViewModel Vm, IStartupManagerService Startup) Build(params StartupEntry[] entries)
    {
        var startup = Substitute.For<IStartupManagerService>();
        startup.GetEntriesAsync(Arg.Any<bool>()).Returns(Task.FromResult(entries.ToList()));

        var boot = Substitute.For<IBootPerformanceService>();
        boot.GetAsync().Returns(Task.FromResult(new BootPerformanceReport()));

        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());

        var vm = new StartupViewModel(startup, settings, Substitute.For<ISnackbarService>(),
            Substitute.For<IContentDialogService>(), boot);
        return (vm, startup);
    }

    [Fact]
    public async Task RefreshCommand_LoadsAllEntriesAndBuildsSummary()
    {
        var (vm, _) = Build(
            Entry("Steam", "Valve", enabled: true),
            Entry("OneDrive", "Microsoft", enabled: false));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("2 startup items · 1 enabled · 1 disabled", vm.SummaryText);
    }

    [Fact]
    public async Task FilterIndex_Enabled_ShowsOnlyEnabledEntries()
    {
        var (vm, _) = Build(
            Entry("Steam", "Valve", enabled: true),
            Entry("OneDrive", "Microsoft", enabled: false));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.FilterIndex = 1;

        Assert.Single(vm.Items);
        Assert.Equal("Steam", vm.Items[0].Name);
    }

    [Fact]
    public async Task FilterIndex_Disabled_ShowsOnlyDisabledEntries()
    {
        var (vm, _) = Build(
            Entry("Steam", "Valve", enabled: true),
            Entry("OneDrive", "Microsoft", enabled: false));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.FilterIndex = 2;

        Assert.Single(vm.Items);
        Assert.Equal("OneDrive", vm.Items[0].Name);
    }

    [Fact]
    public async Task SearchText_MatchesNamePublisherOrCommand_CaseInsensitive()
    {
        var (vm, _) = Build(
            Entry("Steam", "Valve Corp", enabled: true, command: @"C:\Steam\steam.exe"),
            Entry("OneDrive", "Microsoft", enabled: true, command: @"C:\OneDrive\onedrive.exe"));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.SearchText = "valve";

        Assert.Single(vm.Items);
        Assert.Equal("Steam", vm.Items[0].Name);
    }

    [Fact]
    public async Task SearchText_NoMatches_ReturnsEmptyItemsButKeepsSummary()
    {
        var (vm, _) = Build(Entry("Steam", "Valve", enabled: true));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.SearchText = "nonexistent";

        Assert.Empty(vm.Items);
        Assert.Equal("1 startup items · 1 enabled · 0 disabled", vm.SummaryText);
    }

    [Fact]
    public async Task ToggleIsEnabled_StoreWriteSucceeds_UpdatesSummary()
    {
        var (vm, startup) = Build(Entry("Steam", "Valve", enabled: true));
        startup.SetEnabled(Arg.Any<StartupEntry>(), Arg.Any<bool>()).Returns(true);
        await vm.RefreshCommand.ExecuteAsync(null);
        var item = vm.Items[0];

        item.IsEnabled = false;

        Assert.False(item.IsEnabled);
        Assert.Equal("1 startup items · 0 enabled · 1 disabled", vm.SummaryText);
    }

    [Fact]
    public async Task ToggleIsEnabled_StoreWriteFails_RevertsToOriginalValue()
    {
        var (vm, startup) = Build(Entry("Steam", "Valve", enabled: true));
        startup.SetEnabled(Arg.Any<StartupEntry>(), Arg.Any<bool>()).Returns(false);
        await vm.RefreshCommand.ExecuteAsync(null);
        var item = vm.Items[0];

        item.IsEnabled = false;

        Assert.True(item.IsEnabled);
    }
}
