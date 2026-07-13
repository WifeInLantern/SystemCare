using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="ResourceAlertService"/> wires <see cref="ILiveMetricsService"/>'s sampler to alert
/// notifications. The sustained-breach timing itself is covered by <c>ResourceAlertEvaluatorTests</c>;
/// these tests cover the orchestration — subscribe/unsubscribe lifecycle and the enabled/disabled gate.
/// </summary>
public class ResourceAlertServiceTests
{
    private static (ResourceAlertService Service, ILiveMetricsService Metrics, ISettingsService Settings,
        ITrayIconService Tray) Build(AppSettings? settings = null)
    {
        var metrics = Substitute.For<ILiveMetricsService>();
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(settings ?? new AppSettings());
        var snackbar = Substitute.For<ISnackbarService>();
        var tray = Substitute.For<ITrayIconService>();
        // 2.16: temperature source. The substitute returns an empty list, so temperature
        // checks are exercised as "sensors unavailable" — no thermal alerts in these tests.
        var temperature = Substitute.For<ITemperatureService>();
        var service = new ResourceAlertService(metrics, settingsService, snackbar, tray, temperature);
        return (service, metrics, settingsService, tray);
    }

    [Fact]
    public void Start_SubscribesToLiveMetrics()
    {
        var (service, metrics, _, _) = Build();

        service.Start();

        metrics.Received(1).AddConsumer();
    }

    [Fact]
    public void Start_CalledTwice_OnlySubscribesOnce()
    {
        var (service, metrics, _, _) = Build();

        service.Start();
        service.Start();

        metrics.Received(1).AddConsumer();
    }

    [Fact]
    public void Stop_AfterStart_Unsubscribes()
    {
        var (service, metrics, _, _) = Build();
        service.Start();

        service.Stop();

        metrics.Received(1).RemoveConsumer();
    }

    [Fact]
    public void Stop_WithoutStart_DoesNotUnsubscribe()
    {
        var (service, metrics, _, _) = Build();

        service.Stop();

        metrics.DidNotReceive().RemoveConsumer();
    }

    [Fact]
    public void MetricsUpdated_WhenAlertsDisabled_NeverRaisesBalloon()
    {
        var settings = new AppSettings { ResourceAlertsEnabled = false, CpuAlertThresholdPercent = 1, AlertSustainedMinutes = 0 };
        var (service, metrics, _, tray) = Build(settings);
        metrics.Current.Returns(new SystemSnapshot { CpuPercent = 99 });
        service.Start();

        metrics.Updated += Raise.Event<EventHandler>(metrics, EventArgs.Empty);

        tray.DidNotReceive().ShowBalloon(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void MetricsUpdated_WhenCurrentSnapshotMissing_DoesNotThrow()
    {
        var settings = new AppSettings { ResourceAlertsEnabled = true };
        var (service, metrics, _, _) = Build(settings);
        metrics.Current.Returns((SystemSnapshot?)null);
        service.Start();

        var ex = Record.Exception(() => metrics.Updated += Raise.Event<EventHandler>(metrics, EventArgs.Empty));

        Assert.Null(ex);
    }
}
