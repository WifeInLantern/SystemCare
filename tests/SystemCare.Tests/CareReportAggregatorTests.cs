using SystemCare.Helpers;
using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="CareReportAggregator"/> buckets history entries into LOCAL calendar days/weeks
/// (entries store UTC), zero-fills missing buckets, ignores negative byte values, counts zero-byte
/// actions in the action totals but not the byte totals, and orders the category breakdown by
/// frequency.
/// </summary>
public class CareReportAggregatorTests
{
    private static readonly DateTime Today = new(2026, 7, 2); // fixed local "today" for determinism

    private static HistoryEntry At(DateTime localDay, long bytes, string category = "Junk cleanup") => new()
    {
        TimestampUtc = DateTime.SpecifyKind(localDay, DateTimeKind.Local).AddHours(14).ToUniversalTime(),
        Category = category,
        Summary = "test",
        BytesFreed = bytes,
    };

    [Fact]
    public void DailyBytesFreed_BucketsByLocalDay_AndZeroFills()
    {
        var entries = new List<HistoryEntry>
        {
            At(Today, 100),
            At(Today, 50),               // same day accumulates
            At(Today.AddDays(-2), 300),
            At(Today.AddDays(-40), 999), // outside the window — ignored
        };

        var buckets = CareReportAggregator.DailyBytesFreed(entries, days: 7, todayLocal: Today);

        Assert.Equal(7, buckets.Count);
        Assert.Equal(Today.AddDays(-6), buckets[0].Day);
        Assert.Equal(Today, buckets[^1].Day);
        Assert.Equal(150, buckets[^1].Bytes);
        Assert.Equal(300, buckets[4].Bytes);   // two days ago
        Assert.Equal(0, buckets[0].Bytes);     // zero-filled
    }

    [Fact]
    public void DailyBytesFreed_LateEveningEntry_LandsOnItsLocalDay()
    {
        // 23:30 local on the day before "today" — in many UTC offsets this crosses midnight in UTC.
        var lateEvening = new HistoryEntry
        {
            TimestampUtc = DateTime.SpecifyKind(Today.AddDays(-1).AddHours(23.5), DateTimeKind.Local).ToUniversalTime(),
            Category = "Junk cleanup",
            BytesFreed = 42,
        };

        var buckets = CareReportAggregator.DailyBytesFreed([lateEvening], days: 3, todayLocal: Today);

        Assert.Equal(42, buckets[1].Bytes); // yesterday's bucket, not today's
        Assert.Equal(0, buckets[2].Bytes);
    }

    [Fact]
    public void WeeklyBytesFreed_GroupsIntoSevenDayWindows()
    {
        var entries = new List<HistoryEntry>
        {
            At(Today, 100),               // newest week
            At(Today.AddDays(-6), 25),    // still the newest 7-day window
            At(Today.AddDays(-7), 500),   // previous window
        };

        var buckets = CareReportAggregator.WeeklyBytesFreed(entries, weeks: 4, todayLocal: Today);

        Assert.Equal(4, buckets.Count);
        Assert.Equal(125, buckets[^1].Bytes);
        Assert.Equal(500, buckets[^2].Bytes);
        Assert.Equal(0, buckets[0].Bytes);
    }

    [Fact]
    public void CategoryBreakdown_OrdersByCount_ThenBytes()
    {
        var entries = new List<HistoryEntry>
        {
            At(Today, 100, "Junk cleanup"),
            At(Today, 200, "Junk cleanup"),
            At(Today, 0, "Benchmark"),
            At(Today, 0, "Benchmark"),
            At(Today, 5000, "Deep cleanup"),
        };

        var rows = CareReportAggregator.CategoryBreakdown(entries);

        Assert.Equal(3, rows.Count);
        Assert.Equal("Junk cleanup", rows[0].Category);   // 2 actions, 300 bytes beats Benchmark's 0
        Assert.Equal(300, rows[0].Bytes);
        Assert.Equal("Benchmark", rows[1].Category);      // 2 actions, ties on count, fewer bytes
        Assert.Equal("Deep cleanup", rows[2].Category);
    }

    [Fact]
    public void Totals_CountsZeroByteActions_ButNotNegativeBytes()
    {
        var entries = new List<HistoryEntry>
        {
            At(Today, 100),
            At(Today, 0, "Benchmark"),
            At(Today, -50, "Weird"), // defensive: never let a bad entry shrink the total
        };

        var totals = CareReportAggregator.Totals(entries);

        Assert.Equal(100, totals.TotalBytes);
        Assert.Equal(3, totals.TotalActions);
        Assert.NotNull(totals.OldestUtc);
    }

    [Fact]
    public void EmptyHistory_ProducesZeroFilledBucketsAndNullOldest()
    {
        var buckets = CareReportAggregator.DailyBytesFreed([], days: 5, todayLocal: Today);
        Assert.Equal(5, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(0, b.Bytes));

        var totals = CareReportAggregator.Totals([]);
        Assert.Equal(0, totals.TotalActions);
        Assert.Null(totals.OldestUtc);
    }
}
