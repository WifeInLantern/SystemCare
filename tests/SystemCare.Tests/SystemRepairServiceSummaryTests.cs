using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="SystemRepairService"/>'s static summarizers turn an exit code + captured console output into
/// a plain-language <see cref="RepairOutcome"/>. Exit code is the primary signal; output-text phrases are a
/// secondary, locale-dependent hint — these tests pin that priority.
/// </summary>
public class SystemRepairServiceSummaryTests
{
    [Fact]
    public void SummarizeSfc_NonZeroExitCode_IsUnknown()
    {
        var (outcome, _) = SystemRepairService.SummarizeSfc(1, "did not find any integrity violations");

        Assert.Equal(RepairOutcome.Unknown, outcome);
    }

    [Fact]
    public void SummarizeSfc_NoViolationsFound_IsHealthy()
    {
        var (outcome, _) = SystemRepairService.SummarizeSfc(0, "Windows Resource Protection did not find any integrity violations.");

        Assert.Equal(RepairOutcome.Healthy, outcome);
    }

    [Fact]
    public void SummarizeSfc_SuccessfullyRepaired_IsRepaired()
    {
        var (outcome, _) = SystemRepairService.SummarizeSfc(0, "...found corrupt files and successfully repaired them.");

        Assert.Equal(RepairOutcome.Repaired, outcome);
    }

    [Fact]
    public void SummarizeSfc_UnableToFix_NeedsAttention()
    {
        var (outcome, _) = SystemRepairService.SummarizeSfc(0, "...found corrupt files but was unable to fix some of them.");

        Assert.Equal(RepairOutcome.NeedsAttention, outcome);
    }

    [Fact]
    public void SummarizeSfc_UnrecognizedOutput_IsUnknown()
    {
        var (outcome, _) = SystemRepairService.SummarizeSfc(0, "some unexpected text");

        Assert.Equal(RepairOutcome.Unknown, outcome);
    }

    [Fact]
    public void SummarizeDism_NonZeroExitCode_NeedsAttention()
    {
        var (outcome, _) = SystemRepairService.SummarizeDism(1726, "");

        Assert.Equal(RepairOutcome.NeedsAttention, outcome);
    }

    [Fact]
    public void SummarizeDism_NoCorruptionDetected_IsHealthy()
    {
        var (outcome, _) = SystemRepairService.SummarizeDism(0, "No component store corruption detected.");

        Assert.Equal(RepairOutcome.Healthy, outcome);
    }

    [Fact]
    public void SummarizeDism_RestoreCompleted_IsRepaired()
    {
        var (outcome, _) = SystemRepairService.SummarizeDism(0, "The restore operation completed successfully.");

        Assert.Equal(RepairOutcome.Repaired, outcome);
    }

    [Fact]
    public void SummarizeChkdsk_ScheduledForRestart_TakesPriorityOverExitCode()
    {
        var (outcome, _) = SystemRepairService.SummarizeChkdsk(1,
            "Windows has scheduled this volume to be checked the next time the computer restarts.");

        Assert.Equal(RepairOutcome.ScheduledForRestart, outcome);
    }

    [Fact]
    public void SummarizeChkdsk_NonZeroExitCode_NeedsAttention()
    {
        var (outcome, _) = SystemRepairService.SummarizeChkdsk(2, "");

        Assert.Equal(RepairOutcome.NeedsAttention, outcome);
    }

    [Fact]
    public void SummarizeChkdsk_FoundNoProblems_IsHealthy()
    {
        var (outcome, _) = SystemRepairService.SummarizeChkdsk(0, "Windows has checked the file system and found no problems.");

        Assert.Equal(RepairOutcome.Healthy, outcome);
    }

    [Fact]
    public void SummarizeChkdsk_ZeroExitOtherwise_IsRepaired()
    {
        var (outcome, _) = SystemRepairService.SummarizeChkdsk(0, "made corrections to the file system.");

        Assert.Equal(RepairOutcome.Repaired, outcome);
    }
}
