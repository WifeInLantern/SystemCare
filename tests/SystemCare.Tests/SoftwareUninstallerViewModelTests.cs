using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="SoftwareUninstallerViewModel"/> filters the installed-apps list by name/publisher search
/// and reports an aggregate size summary. These tests mock <see cref="IInstalledAppsService"/> and
/// <see cref="IFileOperationService"/> so the filtering/summary logic and the "open install folder"
/// command can be verified without touching the real registry or filesystem.
/// </summary>
public class SoftwareUninstallerViewModelTests
{
    private static InstalledApp App(string name, string publisher, long sizeBytes = 0, string? installLocation = null) => new()
    {
        Name = name,
        Publisher = publisher,
        SizeBytes = sizeBytes,
        InstallLocation = installLocation,
        UninstallString = @"C:\uninstall.exe",
    };

    private static (SoftwareUninstallerViewModel Vm, IInstalledAppsService Apps, IFileOperationService FileOps) Build(params InstalledApp[] apps)
    {
        var appsService = Substitute.For<IInstalledAppsService>();
        appsService.GetInstalledAppsAsync().Returns(Task.FromResult(apps.ToList()));
        var fileOps = Substitute.For<IFileOperationService>();

        var vm = new SoftwareUninstallerViewModel(appsService, fileOps, Substitute.For<ILeftoverScanService>(),
            Substitute.For<ISnackbarService>(), Substitute.For<IContentDialogService>(), Substitute.For<IHistoryService>());
        return (vm, appsService, fileOps);
    }

    [Fact]
    public async Task RefreshCommand_LoadsAppsAndBuildsSizeSummary()
    {
        var (vm, _, _) = Build(
            App("Chrome", "Google", sizeBytes: 200L * 1024 * 1024),
            App("VLC", "VideoLAN", sizeBytes: 50L * 1024 * 1024));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.StartsWith("2 programs", vm.SummaryText);
    }

    [Fact]
    public async Task SearchText_FiltersByNameOrPublisher_CaseInsensitive()
    {
        var (vm, _, _) = Build(
            App("Chrome", "Google LLC"),
            App("Visual Studio Code", "Microsoft"));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.SearchText = "google";

        Assert.Single(vm.Items);
        Assert.Equal("Chrome", vm.Items[0].Name);
    }

    [Fact]
    public async Task SearchText_Cleared_RestoresFullList()
    {
        var (vm, _, _) = Build(App("Chrome", "Google"), App("VLC", "VideoLAN"));
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SearchText = "chrome";

        vm.SearchText = "";

        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task OpenLocationCommand_WithInstallLocation_OpensExplorer()
    {
        var (vm, _, fileOps) = Build(App("Chrome", "Google", installLocation: @"C:\Program Files\Chrome"));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.OpenLocationCommand.Execute(vm.Items[0]);

        fileOps.Received(1).OpenInExplorer(@"C:\Program Files\Chrome");
    }

    [Fact]
    public async Task OpenLocationCommand_WithoutInstallLocation_DoesNotCallFileOps()
    {
        var (vm, _, fileOps) = Build(App("Chrome", "Google", installLocation: null));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.OpenLocationCommand.Execute(vm.Items[0]);

        fileOps.DidNotReceiveWithAnyArgs().OpenInExplorer(default!);
    }
}
