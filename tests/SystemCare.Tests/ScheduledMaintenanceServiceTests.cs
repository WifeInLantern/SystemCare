using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="ScheduledMaintenanceService.RunMaintenanceNowAsync"/> runs only the steps the
/// maintenance profile enables, fault-isolates each step (one failure must not abort the rest),
/// and composes an honest <see cref="MaintenanceResult.Summary"/>. <c>Sync()</c>/<c>TaskExists()</c>
/// construct a real <c>TaskService</c> and stay untested, consistent with the rest of the suite.
/// </summary>
public class ScheduledMaintenanceServiceTests
{
    private sealed record Harness(
        ScheduledMaintenanceService Svc,
        IJunkScanService Junk,
        IMemoryOptimizerService Ram,
        INetworkToolsService Net,
        IRecycleBinService Bin,
        ISettingsService Settings,
        IHistoryService History);

    private static Harness Build(AppSettings? appSettings = null)
    {
        var junk = Substitute.For<IJunkScanService>();
        junk.Categories.Returns(new List<JunkCategory>
        {
            new() { Id = "temp", Name = "Temp files", Description = "" },
        });
        junk.ScanAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<ScanProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new JunkScanResult());
        junk.CleanAsync(Arg.Any<JunkScanResult>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<ScanProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new CleanResult { BytesRemoved = 10 * 1024 * 1024, FilesRemoved = 42 });

        var ram = Substitute.For<IMemoryOptimizerService>();
        ram.OptimizeAsync().Returns(new MemoryOptimizeResult { BytesFreed = 512 * 1024 * 1024, ProcessesTrimmed = 7 });

        var net = Substitute.For<INetworkToolsService>();
        net.FlushDns().Returns("Flushed.");

        var bin = Substitute.For<IRecycleBinService>();
        bin.Query().Returns((2L * 1024 * 1024 * 1024, 15L));

        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(appSettings ?? new AppSettings());

        var history = Substitute.For<IHistoryService>();

        var svc = new ScheduledMaintenanceService(junk, ram, net, bin, settings, history,
            Substitute.For<ILogService>());
        return new Harness(svc, junk, ram, net, bin, settings, history);
    }

    [Fact]
    public async Task DefaultProfile_RunsJunkAndRamOnly()
    {
        var h = Build(); // AppSettings defaults: junk + RAM on, DNS + bin off

        var result = await h.Svc.RunMaintenanceNowAsync();

        Assert.True(result.JunkCleaned);
        Assert.True(result.RamTrimmed);
        Assert.False(result.DnsFlushed);
        Assert.False(result.RecycleBinEmptied);
        h.Net.DidNotReceive().FlushDns();
        h.Bin.DidNotReceive().Empty();
        Assert.Equal(10 * 1024 * 1024, result.BytesRemoved);
        Assert.Equal(512 * 1024 * 1024, result.BytesFreed);
    }

    [Fact]
    public async Task FullProfile_RunsEveryStep_AndSumsHistoryBytes()
    {
        var h = Build(new AppSettings
        {
            MaintenanceCleanJunk = true,
            MaintenanceTrimRam = true,
            MaintenanceFlushDns = true,
            MaintenanceEmptyRecycleBin = true,
        });

        var result = await h.Svc.RunMaintenanceNowAsync();

        Assert.True(result.DnsFlushed);
        Assert.True(result.RecycleBinEmptied);
        Assert.Equal(2L * 1024 * 1024 * 1024, result.RecycleBinBytes);
        h.Bin.Received(1).Empty();

        // One history entry whose byte total covers junk + recycle bin.
        h.History.Received(1).Record("Auto maintenance", Arg.Any<string>(),
            10 * 1024 * 1024 + 2L * 1024 * 1024 * 1024, 42, Arg.Any<string>());
    }

    [Fact]
    public async Task AllStepsDisabled_RunsNothing_AndSaysSo()
    {
        var h = Build(new AppSettings
        {
            MaintenanceCleanJunk = false,
            MaintenanceTrimRam = false,
        });

        var result = await h.Svc.RunMaintenanceNowAsync();

        await h.Junk.DidNotReceiveWithAnyArgs().ScanAsync(default!, default, default);
        await h.Ram.DidNotReceive().OptimizeAsync();
        Assert.Equal("No maintenance steps ran.", result.Summary);
    }

    [Fact]
    public async Task ExplicitProfile_OverridesSettings()
    {
        // Settings say nothing runs; the caller (Disk Health one-click flow) forces junk + RAM.
        var h = Build(new AppSettings { MaintenanceCleanJunk = false, MaintenanceTrimRam = false });

        var result = await h.Svc.RunMaintenanceNowAsync(MaintenanceProfile.JunkAndRam);

        Assert.True(result.JunkCleaned);
        Assert.True(result.RamTrimmed);
    }

    [Fact]
    public async Task JunkStepThrows_RemainingStepsStillRun()
    {
        var h = Build(new AppSettings { MaintenanceFlushDns = true, MaintenanceEmptyRecycleBin = true });
        h.Junk.ScanAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<ScanProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("temp dir locked"));

        var result = await h.Svc.RunMaintenanceNowAsync();

        Assert.False(result.JunkCleaned);
        Assert.True(result.RamTrimmed);
        Assert.True(result.DnsFlushed);
        Assert.True(result.RecycleBinEmptied);
        Assert.DoesNotContain("junk", result.Summary); // summary must not claim the failed step
    }

    [Fact]
    public async Task EmptyRecycleBin_AlreadyEmpty_SkipsEmptyCallAndSummaryMention()
    {
        var h = Build(new AppSettings
        {
            MaintenanceCleanJunk = false,
            MaintenanceTrimRam = false,
            MaintenanceEmptyRecycleBin = true,
        });
        h.Bin.Query().Returns((0L, 0L));

        var result = await h.Svc.RunMaintenanceNowAsync();

        h.Bin.DidNotReceive().Empty();
        Assert.False(result.RecycleBinEmptied);
    }

    [Fact]
    public void Summary_ComposesOnlyStepsThatRan()
    {
        var result = new MaintenanceResult
        {
            JunkCleaned = true,
            BytesRemoved = 1024 * 1024,
            DnsFlushed = true,
        };

        Assert.Equal("Cleaned 1 MB of junk · flushed DNS", result.Summary);
    }

    [Fact]
    public async Task RunUpdatesLastScanTimestamp()
    {
        var h = Build();
        await h.Svc.RunMaintenanceNowAsync();

        Assert.NotNull(h.Settings.Current.LastScanUtc);
        h.Settings.Received(1).Save();
    }
}
