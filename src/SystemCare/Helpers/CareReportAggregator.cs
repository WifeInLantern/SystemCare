using SystemCare.Models;

namespace SystemCare.Helpers;

/// <summary>
/// Pure aggregation over the activity history for the Care Report page and HTML export.
/// Buckets use LOCAL calendar days (entries store UTC timestamps, but users read local dates).
/// Only byte-bearing entries contribute to byte totals; zero-byte actions (installs, benchmarks…)
/// still count as actions.
/// </summary>
internal static class CareReportAggregator
{
    public record DayBucket(DateTime Day, long Bytes);
    public record WeekBucket(DateTime WeekStart, long Bytes);
    public record CategoryRow(string Category, int Count, long Bytes);
    public record HistoryTotals(long TotalBytes, int TotalActions, DateTime? OldestUtc);

    /// <summary>The last <paramref name="days"/> local days (oldest-first, zero-filled).</summary>
    public static IReadOnlyList<DayBucket> DailyBytesFreed(
        IReadOnlyList<HistoryEntry> entries, int days, DateTime? todayLocal = null)
    {
        var today = (todayLocal ?? DateTime.Now).Date;
        var start = today.AddDays(-(days - 1));

        var byDay = new Dictionary<DateTime, long>();
        foreach (var e in entries)
        {
            var day = e.TimestampUtc.ToLocalTime().Date;
            if (day < start || day > today) continue;
            byDay[day] = byDay.GetValueOrDefault(day) + Math.Max(0, e.BytesFreed);
        }

        var list = new List<DayBucket>(days);
        for (var d = start; d <= today; d = d.AddDays(1))
            list.Add(new DayBucket(d, byDay.GetValueOrDefault(d)));
        return list;
    }

    /// <summary>The last <paramref name="weeks"/> 7-day windows ending today (oldest-first, zero-filled).</summary>
    public static IReadOnlyList<WeekBucket> WeeklyBytesFreed(
        IReadOnlyList<HistoryEntry> entries, int weeks, DateTime? todayLocal = null)
    {
        var today = (todayLocal ?? DateTime.Now).Date;
        var start = today.AddDays(-(weeks * 7 - 1));

        var totals = new long[weeks];
        foreach (var e in entries)
        {
            var day = e.TimestampUtc.ToLocalTime().Date;
            if (day < start || day > today) continue;
            int index = (int)((day - start).Days / 7);
            totals[Math.Min(index, weeks - 1)] += Math.Max(0, e.BytesFreed);
        }

        var list = new List<WeekBucket>(weeks);
        for (int i = 0; i < weeks; i++)
            list.Add(new WeekBucket(start.AddDays(i * 7), totals[i]));
        return list;
    }

    /// <summary>Actions grouped by category, most frequent first.</summary>
    public static IReadOnlyList<CategoryRow> CategoryBreakdown(IReadOnlyList<HistoryEntry> entries) =>
        entries
            .GroupBy(e => e.Category)
            .Select(g => new CategoryRow(g.Key, g.Count(), g.Sum(e => Math.Max(0, e.BytesFreed))))
            .OrderByDescending(r => r.Count)
            .ThenByDescending(r => r.Bytes)
            .ToList();

    public static HistoryTotals Totals(IReadOnlyList<HistoryEntry> entries) =>
        new(entries.Sum(e => Math.Max(0, e.BytesFreed)),
            entries.Count,
            entries.Count == 0 ? null : entries.Min(e => e.TimestampUtc));
}
