using NSubstitute;
using SystemCare.Services;
using SystemCare.ViewModels;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="NetworkMonitorViewModel"/> lifecycle around the ETW session: an unavailable service
/// surfaces its status text instead of rows, and stopping tears down state. The per-tick rate math
/// lives in <see cref="Helpers.NetRateCalculator"/> and is covered by <see cref="NetRateCalculatorTests"/>;
/// the DispatcherTimer itself is not driven in unit tests (consistent with the rest of the suite).
/// </summary>
public class NetworkMonitorViewModelTests
{
    [Fact]
    public void StartMonitoring_ServiceUnavailable_SurfacesStatusAndNoRows()
    {
        var usage = Substitute.For<INetworkUsageService>();
        usage.IsAvailable.Returns(false);
        usage.StatusMessage.Returns("Per-process network monitoring needs administrator rights.");
        var vm = new NetworkMonitorViewModel(usage);

        vm.StartMonitoring();

        usage.Received(1).Start();
        Assert.False(vm.MonitoringAvailable);
        Assert.True(vm.MonitoringUnavailable);
        Assert.Equal("Per-process network monitoring needs administrator rights.", vm.MonitoringStatus);
        Assert.Empty(vm.Processes);
    }

    [Fact]
    public void StartMonitoring_ServiceAvailable_ResetsSessionState()
    {
        var usage = Substitute.For<INetworkUsageService>();
        usage.IsAvailable.Returns(true);
        var vm = new NetworkMonitorViewModel(usage);

        vm.StartMonitoring();

        Assert.True(vm.MonitoringAvailable);
        Assert.Equal("Listening for activity…", vm.MonitoringStatus);
        Assert.Equal("0 B/s", vm.TotalRateText);
        Assert.Equal("0 B", vm.SessionDownText);
        Assert.Equal("0 B", vm.SessionUpText);
        Assert.Contains("totals reset", vm.SessionInfo);

        vm.StopMonitoring();
        usage.Received(1).Stop();
        Assert.Empty(vm.Processes);
    }

    [Fact]
    public void SortToggles_AreMutuallyReflectedInSortMode()
    {
        var vm = new NetworkMonitorViewModel(Substitute.For<INetworkUsageService>());

        Assert.Equal(NetworkSortMode.Combined, vm.SortMode);

        vm.IsSortDownload = true;
        Assert.Equal(NetworkSortMode.Download, vm.SortMode);

        vm.IsSortUpload = true;
        Assert.Equal(NetworkSortMode.Upload, vm.SortMode);
    }
}
