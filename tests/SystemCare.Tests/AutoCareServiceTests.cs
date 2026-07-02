using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="AutoCareService"/> orchestrates the read-only probes: it must respect the user's
/// junk-category toggles, tolerate a failing/missing winget (probe reports -1, analysis still
/// succeeds), honour software-update exclusions, and feed the real health score into the
/// recommendations.
/// </summary>
public class AutoCareServiceTests
{
    private sealed record Harness(
        AutoCareService Svc,
        IJunkScanService Junk,
        IStartupManagerService Startup,
        ISecurityCheckService Security,
        ISoftwareUpdateService Updates,
        ISettingsService Settings);

    private static Harness Build(AppSettings? appSettings = null)
    {
        var junk = Substitute.For<IJunkScanService>();
        junk.Categories.Returns(new List<JunkCategory>
        {
            new() { Id = "temp", Name = "Temp", Description = "" },
            new() { Id = "browser", Name = "Browser", Description = "" },
        });
        junk.ScanAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<ScanProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new JunkScanResult());

        var startup = Substitute.For<IStartupManagerService>();
        startup.GetEntriesAsync(false).Returns(new List<StartupEntry>());

        var security = Substitute.For<ISecurityCheckService>();
        security.GetChecksAsync().Returns(new List<SecurityCheck>());

        var updates = Substitute.For<ISoftwareUpdateService>();
        updates.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        updates.GetUpgradesAsync(Arg.Any<CancellationToken>()).Returns(new List<SoftwareUpdate>());

        var systemInfo = Substitute.For<ISystemInfoService>();
        systemInfo.GetSnapshot(Arg.Any<bool>()).Returns(new SystemSnapshot { RamLoadPercent = 30 });

        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(appSettings ?? new AppSettings());

        var svc = new AutoCareService(junk, startup, security, updates, systemInfo,
            new HealthScoreService(), settings, Substitute.For<ILogService>());
        return new Harness(svc, junk, startup, security, updates, settings);
    }

    private static SoftwareUpdate Update(string id) => new() { Name = id, Id = id };

    [Fact]
    public async Task Analyze_HealthyPc_NoRecommendations_ScoresHundred()
    {
        var h = Build();

        var analysis = await h.Svc.AnalyzeAsync(null, CancellationToken.None);

        Assert.Empty(analysis.Recommendations);
        Assert.Equal(100, analysis.Probes.Health.Score);
        Assert.Equal(0, analysis.Probes.PendingSoftwareUpdates);
    }

    [Fact]
    public async Task Analyze_RespectsJunkCategoryToggles()
    {
        var settings = new AppSettings();
        settings.JunkCategoryToggles["browser"] = false;
        var h = Build(settings);

        var analysis = await h.Svc.AnalyzeAsync(null, CancellationToken.None);

        Assert.Equal(["temp"], analysis.JunkCategoryIds);
        await h.Junk.Received(1).ScanAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "temp" })),
            Arg.Any<IProgress<ScanProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Analyze_WingetMissing_ReportsProbeUnavailable_WithoutListingUpgrades()
    {
        var h = Build();
        h.Updates.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        var analysis = await h.Svc.AnalyzeAsync(null, CancellationToken.None);

        Assert.Equal(-1, analysis.Probes.PendingSoftwareUpdates);
        await h.Updates.DidNotReceiveWithAnyArgs().GetUpgradesAsync(default);
        Assert.DoesNotContain(analysis.Recommendations, r => r.Id == "updates");
    }

    [Fact]
    public async Task Analyze_UpdatesProbeThrows_AnalysisStillSucceeds()
    {
        var h = Build();
        h.Updates.GetUpgradesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("winget exploded"));

        var analysis = await h.Svc.AnalyzeAsync(null, CancellationToken.None);

        Assert.Equal(-1, analysis.Probes.PendingSoftwareUpdates);
    }

    [Fact]
    public async Task Analyze_ExcludedUpdates_DoNotCount()
    {
        var settings = new AppSettings();
        settings.SoftwareUpdateExclusions.Add("Skipped.App");
        var h = Build(settings);
        h.Updates.GetUpgradesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoftwareUpdate> { Update("Skipped.App"), Update("Wanted.App") });

        var analysis = await h.Svc.AnalyzeAsync(null, CancellationToken.None);

        Assert.Equal(1, analysis.Probes.PendingSoftwareUpdates);
    }

    [Fact]
    public async Task Analyze_SecurityWarningsAndBad_CountAsIssues_UnknownDoesNot()
    {
        var h = Build();
        h.Security.GetChecksAsync().Returns(new List<SecurityCheck>
        {
            new() { Name = "Defender", Status = SecurityStatus.Ok },
            new() { Name = "Firewall", Status = SecurityStatus.Warning },
            new() { Name = "UAC", Status = SecurityStatus.Bad },
            new() { Name = "RDP", Status = SecurityStatus.Unknown },
        });

        var analysis = await h.Svc.AnalyzeAsync(null, CancellationToken.None);

        Assert.Equal(2, analysis.Probes.SecurityIssues);
        Assert.Contains(analysis.Recommendations, r => r.Id == "security");
    }
}
