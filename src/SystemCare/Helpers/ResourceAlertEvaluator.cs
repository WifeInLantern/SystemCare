namespace SystemCare.Helpers;

/// <summary>
/// Pure breach-timing logic for <c>ResourceAlertService</c>: decides whether a metric that has been at or
/// above a threshold for a sustained duration should raise an alert, and fires at most once per breach
/// episode (an episode ends — and re-arms — only once the metric drops back below the threshold).
/// Split out so the timing math can be unit tested without a real <c>DispatcherTimer</c> or wall-clock waits.
/// </summary>
public static class ResourceAlertEvaluator
{
    /// <summary>Per-metric state carried between ticks by the caller.</summary>
    public readonly record struct BreachState(DateTime? BreachStartUtc, bool Alerted);

    /// <summary>
    /// Advances <paramref name="state"/> for one sample. Returns the updated state to carry into the next
    /// call, and whether this sample should raise an alert (true at most once per continuous breach).
    /// </summary>
    public static (BreachState NewState, bool ShouldAlert) Evaluate(
        double currentValue, int thresholdPercent, int sustainedMinutes, DateTime nowUtc, BreachState state)
    {
        if (currentValue < thresholdPercent)
            return (new BreachState(null, false), false);

        DateTime breachStartUtc = state.BreachStartUtc ?? nowUtc;
        if (state.Alerted)
            return (new BreachState(breachStartUtc, true), false);

        bool shouldAlert = nowUtc - breachStartUtc >= TimeSpan.FromMinutes(sustainedMinutes);
        return (new BreachState(breachStartUtc, shouldAlert), shouldAlert);
    }
}
