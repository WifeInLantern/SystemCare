using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="StorageForecast"/> is the pure math behind the 2.14 Storage Forecast: it must abstain
/// (return null) whenever a forecast would be meaningless — too few samples, stable or growing free
/// space, horizons too far out to act on — and produce a sane days-until-full estimate for a clean
/// linear decline.
/// </summary>
public class StorageForecastTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static List<(DateTime, long)> DailySamples(params long[] freeBytesPerDay) =>
        freeBytesPerDay.Select((free, day) => (T0.AddDays(day), free)).ToList();

    [Fact]
    public void Abstains_below_minimum_sample_count()
    {
        var samples = DailySamples(100_000_000_000, 99_000_000_000, 98_000_000_000); // 3 < MinSamples(4)
        Assert.Null(StorageForecast.DaysUntilFull(samples));
    }

    [Fact]
    public void Abstains_when_free_space_grows()
    {
        var samples = DailySamples(90, 92, 94, 96, 98).Select(s => (s.Item1, s.Item2 * 1_000_000_000)).ToList();
        Assert.Null(StorageForecast.DaysUntilFull(samples));
    }

    [Fact]
    public void Abstains_when_free_space_is_flat()
    {
        var samples = DailySamples(50_000_000_000, 50_000_000_000, 50_000_000_000, 50_000_000_000);
        Assert.Null(StorageForecast.DaysUntilFull(samples));
    }

    [Fact]
    public void Linear_decline_yields_expected_horizon()
    {
        // Losing 2 GB/day from 100 GB → ~50 days from the last sample (which sits at 92 GB → 46 days).
        var samples = DailySamples(100_000_000_000, 98_000_000_000, 96_000_000_000, 94_000_000_000, 92_000_000_000);
        double? days = StorageForecast.DaysUntilFull(samples);
        Assert.NotNull(days);
        Assert.InRange(days!.Value, 44, 48);
    }

    [Fact]
    public void Abstains_when_horizon_exceeds_max_days()
    {
        // Losing 1 MB/day from 1 TB → horizon of ~millennia; not actionable.
        var samples = DailySamples(1_000_000_000_000, 999_999_000_000, 999_998_000_000, 999_997_000_000);
        Assert.Null(StorageForecast.DaysUntilFull(samples));
    }

    [Fact]
    public void Abstains_when_all_samples_share_a_timestamp()
    {
        var samples = Enumerable.Range(0, 5).Select(i => (T0, 50_000_000_000L - i)).ToList();
        Assert.Null(StorageForecast.DaysUntilFull(samples));
    }

    [Theory]
    [InlineData(0.5, "Full within a day at the current rate")]
    [InlineData(5, "~5 days until full at the current rate")]
    [InlineData(21, "~3 weeks until full at the current rate")]
    [InlineData(90, "~3 months until full at the current rate")]
    public void Describe_uses_humane_buckets(double days, string expected) =>
        Assert.Equal(expected, StorageForecast.Describe(days));

    [Fact]
    public void Describe_returns_null_for_null() => Assert.Null(StorageForecast.Describe(null));
}
