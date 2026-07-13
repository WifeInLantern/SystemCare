namespace SystemCare.Helpers;

/// <summary>
/// Pure math for the Storage Forecast feature (2.14): a least-squares linear fit over
/// (time, free-bytes) samples, answering "at the current rate, how many days until this
/// drive is full?". Kept free of I/O so it is trivially unit-testable.
/// </summary>
public static class StorageForecast
{
    /// <summary>Minimum samples for a meaningful fit; below this the forecast abstains.</summary>
    public const int MinSamples = 4;

    /// <summary>
    /// Returns the estimated number of days until free space reaches zero, or null when a
    /// forecast is not meaningful: too few samples, free space growing or flat, a fit horizon
    /// beyond <paramref name="maxDays"/> (not actionable), or degenerate timestamps.
    /// </summary>
    public static double? DaysUntilFull(IReadOnlyList<(DateTime TimestampUtc, long FreeBytes)> samples, double maxDays = 365)
    {
        if (samples is null || samples.Count < MinSamples) return null;

        // x = days since the first sample; y = free bytes.
        DateTime origin = samples[0].TimestampUtc;
        int n = samples.Count;
        double sumX = 0, sumY = 0, sumXy = 0, sumXx = 0;
        foreach (var (t, free) in samples)
        {
            double x = (t - origin).TotalDays;
            double y = free;
            sumX += x; sumY += y; sumXy += x * y; sumXx += x * x;
        }

        double denom = n * sumXx - sumX * sumX;
        if (denom <= 0) return null; // all samples at (nearly) the same instant

        double slope = (n * sumXy - sumX * sumY) / denom; // bytes per day
        if (slope >= 0) return null; // free space stable or growing — nothing to warn about

        double lastX = (samples[^1].TimestampUtc - origin).TotalDays;
        double intercept = (sumY - slope * sumX) / n;
        double currentFitFree = intercept + slope * lastX;
        if (currentFitFree <= 0) return 0;

        double days = currentFitFree / -slope;
        return days > maxDays ? null : days;
    }

    /// <summary>Human wording for a forecast, or null when there is nothing worth saying.</summary>
    public static string? Describe(double? daysUntilFull)
    {
        if (daysUntilFull is not double d) return null;
        if (d < 1) return "Full within a day at the current rate";
        if (d < 14) return $"~{Math.Round(d)} days until full at the current rate";
        if (d < 60) return $"~{Math.Round(d / 7)} weeks until full at the current rate";
        return $"~{Math.Round(d / 30)} months until full at the current rate";
    }
}
