using System.Collections.Generic;
using System.Linq;
using SystemCare.Helpers;
using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

/// <summary>Pure formatting helpers behind the tray/mini-widget live monitor.</summary>
public class MetricsFormatterTests
{
    [Theory]
    [InlineData(null, "—")]     // no sample yet
    [InlineData(0.0, "0%")]
    [InlineData(23.4, "23%")]   // rounds
    [InlineData(23.6, "24%")]
    [InlineData(100.0, "100%")]
    public void Cpu_FormatsOrDashes(double? cpu, string expected)
    {
        Assert.Equal(expected, MetricsFormatter.Cpu(cpu));
    }

    [Fact]
    public void Ram_FormatsWholePercent()
    {
        Assert.Equal("41%", MetricsFormatter.Ram(41.2));
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(1536, "1.5 KB/s")]
    [InlineData(-5, "0 B/s")]       // negatives clamp to zero
    public void NetRate_FormatsAndClamps(double bytesPerSec, string expected)
    {
        Assert.Equal(expected, MetricsFormatter.NetRate(bytesPerSec));
    }

    [Fact]
    public void TrayTooltip_NullSnapshot_IsAppNameOnly()
    {
        Assert.Equal("SystemCare", MetricsFormatter.TrayTooltip(null));
    }

    [Fact]
    public void TrayTooltip_IncludesCpuAndRam()
    {
        var snap = new SystemSnapshot { CpuPercent = 23.4, RamLoadPercent = 41 };

        string text = MetricsFormatter.TrayTooltip(snap);

        Assert.Contains("CPU 23%", text);
        Assert.Contains("RAM 41%", text);
    }

    [Fact]
    public void Push_TrimsToCapacityAndKeepsNewest()
    {
        var buffer = new Queue<double>();
        for (int i = 1; i <= 5; i++) MetricsFormatter.Push(buffer, i, capacity: 3);

        Assert.Equal(new double[] { 3, 4, 5 }, buffer.ToArray());
    }
}
