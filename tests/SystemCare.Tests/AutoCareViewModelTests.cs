using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="AutoCareViewModel"/> turns an analysis into recommendation cards and applies direct
/// fixes through the same services the Dashboard uses. Unlike the install flows elsewhere, nothing
/// here is gated by an unmockable dialog — the restore-point prompt goes through
/// <see cref="IBackupConfirmationService"/> — so Apply is fully unit-testable.
/// </summary>
public class AutoCareViewModelTests
{
    private sealed record Harness(
        AutoCareViewModel Vm,
        IAutoCareService AutoCare,
        IJunkScanService Junk,
        IMemoryOptimizerService Ram,
        IBackupConfirmationService Backup,
        IRestorePointService Restore,
        IHistoryService History,
        IHealthTrendService Trend,
        ISettingsService Settings);

    private static Harness Build()
    {
        var autoCare = Substitute.For<IAutoCareService>();
        var junk = Substitute.For<IJunkScanService>();
        junk.CleanAsync(Arg.Any<JunkScanResult>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<ScanProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new CleanResult { BytesRemoved = 1024, FilesRemoved = 2 });
        var ram = Substitute.For<IMemoryOptimizerService>();
        ram.OptimizeAsync().Returns(new MemoryOptimizeResult { BytesFreed = 2048, ProcessesTrimmed = 3 });
        var backup = Substitute.For<IBackupConfirmationService>();
        backup.ConfirmRestorePointAsync(Arg.Any<string>()).Returns(false);
        var restore = Substitute.For<IRestorePointService>();
        var history = Substitute.For<IHistoryService>();
        var trend = Substitute.For<IHealthTrendService>();
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());

        var vm = new AutoCareViewModel(autoCare, junk, ram, settings, trend, backup, restore,
            Substitute.For<ISnackbarService>(), history, Substitute.For<ILogService>());
        return new Harness(vm, autoCare, junk, ram, backup, restore, history, trend, settings);
    }

    private static AutoCareAnalysis Analysis(params Recommendation[] recs)
    {
        var junk = new JunkScanResult();
        junk.Categories.Add(new JunkCategoryResult
        {
            Category = new JunkCategory { Id = "temp", Name = "Temp", Description = "" },
            TotalBytes = 500L * 1024 * 1024,
            FileCount = 10,
        });
        return new AutoCareAnalysis
        {
            Probes = new AutoCareProbeResults { Junk = junk, Health = new HealthReport { Score = 72 } },
            Recommendations = recs,
            JunkCategoryIds = ["temp"],
        };
    }

    private static Recommendation JunkRec() => new()
    {
        Id = "junk", Title = "Clean", Explanation = "x", Action = RecommendationAction.CleanJunk,
        HealthPointsRecoverable = 10,
    };

    private static Recommendation RamRec() => new()
    {
        Id = "ram", Title = "Trim", Explanation = "x", Action = RecommendationAction.TrimRam,
        HealthPointsRecoverable = 8,
    };

    [Fact]
    public async Task Analyze_PopulatesCards_UpdatesScore_AndRecordsTrend()
    {
        var h = Build();
        h.AutoCare.AnalyzeAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Analysis(JunkRec(), RamRec()));

        await h.Vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.True(h.Vm.HasAnalyzed);
        Assert.False(h.Vm.AllClear);
        Assert.Equal(2, h.Vm.Recommendations.Count);
        Assert.Equal(72, h.Vm.HealthScoreValue);
        Assert.Contains("2 recommendation", h.Vm.Headline);
        h.Trend.Received(1).Record(72);
        Assert.Equal(72, h.Settings.Current.LastHealthScore);
    }

    [Fact]
    public async Task Analyze_NoFindings_ShowsAllClear()
    {
        var h = Build();
        h.AutoCare.AnalyzeAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Analysis());

        await h.Vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.True(h.Vm.AllClear);
        Assert.Contains("great shape", h.Vm.Headline);
    }

    [Fact]
    public async Task ApplyJunk_CleansTheStoredScan_WithTheProbedCategoryIds()
    {
        var h = Build();
        var analysis = Analysis(JunkRec());
        h.AutoCare.AnalyzeAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>()).Returns(analysis);
        await h.Vm.AnalyzeCommand.ExecuteAsync(null);
        var card = h.Vm.Recommendations[0];

        await h.Vm.ApplyCommand.ExecuteAsync(card);

        await h.Junk.Received(1).CleanAsync(analysis.Probes.Junk!,
            Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "temp" })),
            Arg.Any<IProgress<ScanProgress>?>(), Arg.Any<CancellationToken>());
        Assert.True(card.IsDone);
        h.History.Received(1).Record("Auto care", Arg.Any<string>(), 1024, 2, Arg.Any<string>());
        await h.Restore.DidNotReceiveWithAnyArgs().CreateRestorePointAsync(default!);
    }

    [Fact]
    public async Task ApplyJunk_WhenBackupConfirmed_CreatesRestorePointFirst()
    {
        var h = Build();
        h.Backup.ConfirmRestorePointAsync(Arg.Any<string>()).Returns(true);
        h.Restore.CreateRestorePointAsync(Arg.Any<string>()).Returns((true, "ok"));
        h.AutoCare.AnalyzeAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Analysis(JunkRec()));
        await h.Vm.AnalyzeCommand.ExecuteAsync(null);

        await h.Vm.ApplyCommand.ExecuteAsync(h.Vm.Recommendations[0]);

        await h.Restore.Received(1).CreateRestorePointAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ApplyRam_TrimsAndMarksDone()
    {
        var h = Build();
        h.AutoCare.AnalyzeAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Analysis(RamRec()));
        await h.Vm.AnalyzeCommand.ExecuteAsync(null);
        var card = h.Vm.Recommendations[0];

        await h.Vm.ApplyCommand.ExecuteAsync(card);

        await h.Ram.Received(1).OptimizeAsync();
        Assert.True(card.IsDone);
    }

    [Fact]
    public async Task Apply_AlreadyDoneCard_DoesNothing()
    {
        var h = Build();
        h.AutoCare.AnalyzeAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Analysis(RamRec()));
        await h.Vm.AnalyzeCommand.ExecuteAsync(null);
        var card = h.Vm.Recommendations[0];
        card.IsDone = true;

        await h.Vm.ApplyCommand.ExecuteAsync(card);

        await h.Ram.DidNotReceive().OptimizeAsync();
    }
}
