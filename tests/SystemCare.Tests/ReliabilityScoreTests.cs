using SystemCare.Helpers;
using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// The pure stability score: penalty weighting by category, clamping, and tier thresholds. The Event-Log
/// reader is interop (verified live).
/// </summary>
public class ReliabilityScoreTests
{
    private static ReliabilityEvent Ev(ReliabilityCategory c) =>
        new(c, ReliabilitySeverity.Error, "test", "test", DateTime.UtcNow);

    [Fact]
    public void NoEvents_IsPerfect()
    {
        Assert.Equal(100, ReliabilityScore.Score([]));
    }

    [Fact]
    public void SevereProblemsCostMoreThanMinorOnes()
    {
        Assert.True(ReliabilityScore.Penalty(ReliabilityCategory.BlueScreen)
                    > ReliabilityScore.Penalty(ReliabilityCategory.AppHang));
        Assert.True(ReliabilityScore.Penalty(ReliabilityCategory.UnexpectedShutdown)
                    > ReliabilityScore.Penalty(ReliabilityCategory.ServiceFailure));
    }

    [Fact]
    public void SingleEvent_SubtractsItsPenalty()
    {
        Assert.Equal(100 - ReliabilityScore.Penalty(ReliabilityCategory.Crash),
            ReliabilityScore.Score([Ev(ReliabilityCategory.Crash)]));
    }

    [Fact]
    public void ManyEvents_ClampAtZero()
    {
        var events = Enumerable.Repeat(Ev(ReliabilityCategory.BlueScreen), 20).ToList();
        Assert.Equal(0, ReliabilityScore.Score(events));
    }

    [Theory]
    [InlineData(100, "Stable")]
    [InlineData(90, "Stable")]
    [InlineData(89, "Mostly stable")]
    [InlineData(70, "Mostly stable")]
    [InlineData(69, "Unstable")]
    [InlineData(40, "Unstable")]
    [InlineData(39, "Critical")]
    [InlineData(0, "Critical")]
    public void Tier_MatchesThresholds(int score, string expected)
    {
        Assert.Equal(expected, ReliabilityScore.Tier(score));
    }
}
