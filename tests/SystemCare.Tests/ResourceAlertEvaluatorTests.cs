using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="ResourceAlertEvaluator.Evaluate"/> decides when a sustained-threshold breach should raise an
/// alert. Pure timing math extracted from <c>ResourceAlertService</c> so it can be exercised with explicit
/// timestamps instead of real wall-clock waits and a live <c>ILiveMetricsService</c> sampler.
/// </summary>
public class ResourceAlertEvaluatorTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValueBelowThreshold_NeverAlerts()
    {
        var (state, alert) = ResourceAlertEvaluator.Evaluate(50, 90, 5, Start, default);

        Assert.False(alert);
        Assert.Null(state.BreachStartUtc);
        Assert.False(state.Alerted);
    }

    [Fact]
    public void ValueAtOrAboveThreshold_StartsBreachButDoesNotAlertImmediately()
    {
        var (state, alert) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start, default);

        Assert.False(alert);
        Assert.Equal(Start, state.BreachStartUtc);
        Assert.False(state.Alerted);
    }

    [Fact]
    public void BreachSustainedPastDuration_Alerts()
    {
        var (afterStart, _) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start, default);
        var (state, alert) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start.AddMinutes(5), afterStart);

        Assert.True(alert);
        Assert.True(state.Alerted);
    }

    [Fact]
    public void BreachNotYetSustainedPastDuration_DoesNotAlert()
    {
        var (afterStart, _) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start, default);
        var (state, alert) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start.AddMinutes(4), afterStart);

        Assert.False(alert);
        Assert.False(state.Alerted);
    }

    [Fact]
    public void AlreadyAlerted_DoesNotAlertAgainWhileStillBreaching()
    {
        var (afterStart, _) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start, default);
        var (afterAlert, firstAlert) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start.AddMinutes(5), afterStart);
        var (state, secondAlert) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start.AddMinutes(10), afterAlert);

        Assert.True(firstAlert);
        Assert.False(secondAlert);
        Assert.True(state.Alerted);
    }

    [Fact]
    public void ValueDropsBelowThreshold_ResetsBreachAndRearmsAlert()
    {
        var (afterStart, _) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start, default);
        var (afterAlert, _) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start.AddMinutes(5), afterStart);
        var (reset, alertOnDrop) = ResourceAlertEvaluator.Evaluate(50, 90, 5, Start.AddMinutes(6), afterAlert);

        Assert.False(alertOnDrop);
        Assert.Null(reset.BreachStartUtc);
        Assert.False(reset.Alerted);

        var (rebreached, _) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start.AddMinutes(7), reset);
        var (state, alertAgain) = ResourceAlertEvaluator.Evaluate(95, 90, 5, Start.AddMinutes(12), rebreached);

        Assert.True(alertAgain);
        Assert.True(state.Alerted);
    }

    [Fact]
    public void ValueExactlyAtThreshold_CountsAsBreach()
    {
        var (state, _) = ResourceAlertEvaluator.Evaluate(90, 90, 5, Start, default);

        Assert.Equal(Start, state.BreachStartUtc);
    }
}
