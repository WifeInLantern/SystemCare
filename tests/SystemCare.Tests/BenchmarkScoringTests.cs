using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// The pure benchmark scoring math: metric→sub-score normalization (reference = 50), the weighted overall
/// index, and the headline points scaling. The measuring engine itself is timing/IO interop (verified live).
/// </summary>
public class BenchmarkScoringTests
{
    [Fact]
    public void SubScore_ReferenceMapsTo50()
    {
        Assert.Equal(50, BenchmarkScoring.SubScore(100, 100), 3);
    }

    [Fact]
    public void SubScore_HalfReferenceMapsTo25()
    {
        Assert.Equal(25, BenchmarkScoring.SubScore(50, 100), 3);
    }

    [Fact]
    public void SubScore_TwiceReferenceMapsTo100()
    {
        Assert.Equal(100, BenchmarkScoring.SubScore(200, 100), 3);
    }

    [Fact]
    public void SubScore_ClampsAtTop()
    {
        Assert.Equal(100, BenchmarkScoring.SubScore(1_000_000, 100), 3);
    }

    [Theory]
    [InlineData(0, 100)]   // zero measured
    [InlineData(100, 0)]   // zero/invalid reference
    [InlineData(-5, 100)]  // negative measured
    public void SubScore_NonPositiveInputs_AreZero(double measured, double reference)
    {
        Assert.Equal(0, BenchmarkScoring.SubScore(measured, reference));
    }

    [Fact]
    public void SubScore_IsMonotonic()
    {
        double low = BenchmarkScoring.SubScore(40, 100);
        double mid = BenchmarkScoring.SubScore(80, 100);
        double high = BenchmarkScoring.SubScore(120, 100);
        Assert.True(low < mid && mid < high);
    }

    [Fact]
    public void TypedScores_UseTheirReferences()
    {
        Assert.Equal(50, BenchmarkScoring.CpuScore(BenchmarkScoring.CpuRefMOps), 3);
        Assert.Equal(50, BenchmarkScoring.RamScore(BenchmarkScoring.RamRefGBps), 3);
        Assert.Equal(50, BenchmarkScoring.DiskScore(BenchmarkScoring.DiskRefMBps), 3);
    }

    [Fact]
    public void Weights_SumToOne()
    {
        Assert.Equal(1.0, BenchmarkScoring.CpuWeight + BenchmarkScoring.RamWeight + BenchmarkScoring.DiskWeight, 6);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(50, 50, 50, 50)]
    [InlineData(100, 100, 100, 100)]
    public void Overall_UniformScores_RoundTrip(double c, double r, double d, double expected)
    {
        Assert.Equal(expected, BenchmarkScoring.Overall(c, r, d), 3);
    }

    [Fact]
    public void Overall_AppliesWeights()
    {
        // Only CPU maxed; CpuWeight = 0.5 → overall 50.
        Assert.Equal(100 * BenchmarkScoring.CpuWeight, BenchmarkScoring.Overall(100, 0, 0), 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(75, 7500)]
    [InlineData(100, 10000)]
    public void Points_ScaleByHundred(double index, int expected)
    {
        Assert.Equal(expected, BenchmarkScoring.Points(index));
    }

    [Fact]
    public void Points_ClampsNegativeToZero()
    {
        Assert.Equal(0, BenchmarkScoring.Points(-10));
    }
}
