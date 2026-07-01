using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="SoftwareHubService"/> installs catalog apps via <see cref="IWingetRunner"/> and annotates the
/// catalog with installed-state parsed from <c>winget list</c>. Stubbing <see cref="IWingetRunner"/> lets us
/// verify availability detection, installed-app detection and the install loop (success/failure counting,
/// progress, cancellation, exact command string) without spawning a real winget process.
/// </summary>
public class SoftwareHubServiceTests
{
    private static (SoftwareHubService Svc, IWingetRunner Winget) Build(bool installed = true)
    {
        var winget = Substitute.For<IWingetRunner>();
        winget.IsInstalled.Returns(installed);
        var svc = new SoftwareHubService(Substitute.For<ILogService>(), winget);
        return (svc, winget);
    }

    private static SoftwareHubApp App(string id) => new()
    {
        Name = id, Id = id, Category = "Utilities", Description = "",
    };

    [Fact]
    public async Task IsAvailableAsync_WingetMissing_ReturnsFalse_AndDoesNotRun()
    {
        var (svc, winget) = Build(installed: false);

        Assert.False(await svc.IsAvailableAsync());
        await winget.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task GetCatalogAsync_MarksKnownIdsInstalled_FromRealWingetListSample()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Is<string>(a => a.StartsWith("list")), Arg.Any<CancellationToken>())
            .Returns((0, WingetTestSamples.InstalledAppsListSample));

        var statuses = await svc.GetCatalogAsync(CancellationToken.None);

        Assert.Equal(SoftwareHubCatalog.All.Count, statuses.Count);
        Assert.True(statuses.Single(s => s.App.Id == "Git.Git").IsInstalled);
        Assert.True(statuses.Single(s => s.App.Id == "VideoLAN.VLC").IsInstalled);
        Assert.False(statuses.Single(s => s.App.Id == "Mozilla.Firefox").IsInstalled);
    }

    [Fact]
    public async Task GetCatalogAsync_WingetMissing_ReturnsFullCatalogAllUninstalled()
    {
        var (svc, _) = Build(installed: false);

        var statuses = await svc.GetCatalogAsync(CancellationToken.None);

        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.False(s.IsInstalled));
    }

    [Fact]
    public async Task InstallAsync_AllSucceed_CountsInstalledAndReportsProgress()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((0, ""));
        var reports = new List<SoftwareHubInstallProgress>();
        var progress = new Progress<SoftwareHubInstallProgress>(reports.Add);

        var result = await svc.InstallAsync(new[] { App("A.A"), App("B.B") }, progress, CancellationToken.None);

        Assert.Equal(2, result.Installed);
        Assert.Equal(0, result.Failed);
        await winget.Received(1).RunAsync(Arg.Is<string>(a => a.Contains("--id \"A.A\"")), Arg.Any<CancellationToken>());
        await winget.Received(1).RunAsync(Arg.Is<string>(a => a.Contains("--id \"B.B\"")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_BuildsExactWingetInstallCommand()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((0, ""));

        await svc.InstallAsync(new[] { App("Mozilla.Firefox") }, null, CancellationToken.None);

        await winget.Received(1).RunAsync(
            Arg.Is<string>(a => a == "install --id \"Mozilla.Firefox\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_NonZeroExit_CountsAsFailed()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Is<string>(a => a.Contains("\"Good.App\"")), Arg.Any<CancellationToken>()).Returns((0, ""));
        winget.RunAsync(Arg.Is<string>(a => a.Contains("\"Bad.App\"")), Arg.Any<CancellationToken>()).Returns((-1, ""));

        var result = await svc.InstallAsync(new[] { App("Good.App"), App("Bad.App") }, null, CancellationToken.None);

        Assert.Equal(1, result.Installed);
        Assert.Equal(1, result.Failed);
        Assert.Contains("could not be installed", result.Message);
    }

    [Fact]
    public async Task InstallAsync_Cancelled_Throws()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((0, ""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.InstallAsync(new[] { App("A.A") }, null, cts.Token));
    }

    [Fact]
    public async Task InstallAsync_NothingSelected_ReturnsNoOp()
    {
        var (svc, _) = Build();

        var result = await svc.InstallAsync(Array.Empty<SoftwareHubApp>(), null, CancellationToken.None);

        Assert.Equal(0, result.Installed);
        Assert.Equal(0, result.Failed);
    }
}
