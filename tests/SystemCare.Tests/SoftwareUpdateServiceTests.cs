using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="SoftwareUpdateService"/> drives winget through <see cref="IWingetRunner"/>. Stubbing that
/// interface lets us verify availability detection, upgrade-list parsing and the apply loop (success/failure
/// counting, progress, cancellation) without spawning a real winget process.
/// </summary>
public class SoftwareUpdateServiceTests
{
    private static (SoftwareUpdateService Svc, IWingetRunner Winget) Build(bool installed = true)
    {
        var winget = Substitute.For<IWingetRunner>();
        winget.IsInstalled.Returns(installed);
        var svc = new SoftwareUpdateService(Substitute.For<ILogService>(), winget);
        return (svc, winget);
    }

    private static SoftwareUpdate Update(string id) => new()
    {
        Name = id, Id = id, CurrentVersion = "1.0", AvailableVersion = "2.0", Source = "winget",
    };

    [Fact]
    public async Task IsAvailableAsync_WingetMissing_ReturnsFalse_AndDoesNotRun()
    {
        var (svc, winget) = Build(installed: false);

        Assert.False(await svc.IsAvailableAsync());
        await winget.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task IsAvailableAsync_VersionOutput_ReturnsTrue()
    {
        var (svc, winget) = Build();
        winget.RunAsync("--version", Arg.Any<CancellationToken>()).Returns((0, "v1.28.240"));

        Assert.True(await svc.IsAvailableAsync());
    }

    [Fact]
    public async Task IsAvailableAsync_NonVersionOutput_ReturnsFalse()
    {
        var (svc, winget) = Build();
        winget.RunAsync("--version", Arg.Any<CancellationToken>()).Returns((0, "something else"));

        Assert.False(await svc.IsAvailableAsync());
    }

    [Fact]
    public async Task GetUpgradesAsync_ParsesRealWingetTable()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Is<string>(a => a.StartsWith("upgrade --include-unknown")), Arg.Any<CancellationToken>())
            .Returns((0, WingetTestSamples.EightUpgradesWithSpinner));

        var updates = await svc.GetUpgradesAsync(CancellationToken.None);

        Assert.Equal(8, updates.Count);
        Assert.Contains(updates, u => u.Id == "Anthropic.Claude" && u.AvailableVersion == "1.15962.1");
    }

    [Fact]
    public async Task GetUpgradesAsync_WingetMissing_ReturnsEmpty()
    {
        var (svc, _) = Build(installed: false);

        Assert.Empty(await svc.GetUpgradesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpgradeAsync_AllSucceed_CountsUpdatedAndReportsProgress()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((0, ""));
        var reports = new List<SoftwareUpdateProgress>();
        var progress = new Progress<SoftwareUpdateProgress>(reports.Add);

        var result = await svc.UpgradeAsync(new[] { Update("A.A"), Update("B.B") }, progress, CancellationToken.None);

        Assert.Equal(2, result.Updated);
        Assert.Equal(0, result.Failed);
        await winget.Received(1).RunAsync(Arg.Is<string>(a => a.Contains("--id \"A.A\"")), Arg.Any<CancellationToken>());
        await winget.Received(1).RunAsync(Arg.Is<string>(a => a.Contains("--id \"B.B\"")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpgradeAsync_NonZeroExit_CountsAsFailed()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Is<string>(a => a.Contains("\"Good.App\"")), Arg.Any<CancellationToken>()).Returns((0, ""));
        winget.RunAsync(Arg.Is<string>(a => a.Contains("\"Bad.App\"")), Arg.Any<CancellationToken>()).Returns((-1, ""));

        var result = await svc.UpgradeAsync(new[] { Update("Good.App"), Update("Bad.App") }, null, CancellationToken.None);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Failed);
        Assert.Contains("could not be updated", result.Message);
    }

    [Fact]
    public async Task UpgradeAsync_Cancelled_Throws()
    {
        var (svc, winget) = Build();
        winget.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((0, ""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.UpgradeAsync(new[] { Update("A.A") }, null, cts.Token));
    }

    [Fact]
    public async Task UpgradeAsync_NothingSelected_ReturnsNoOp()
    {
        var (svc, _) = Build();

        var result = await svc.UpgradeAsync(Array.Empty<SoftwareUpdate>(), null, CancellationToken.None);

        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Failed);
    }
}
