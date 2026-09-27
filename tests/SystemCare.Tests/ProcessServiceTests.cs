using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// The app runs elevated, so ending a critical system process from the Process Manager would bugcheck
/// Windows. These tests pin down that those processes, and SystemCare itself, are always refused.
/// </summary>
public class ProcessServiceTests
{
    [Theory]
    [InlineData("csrss")]
    [InlineData("wininit")]
    [InlineData("WINLOGON")]
    [InlineData("services")]
    [InlineData("lsass")]
    public void IsCritical_RecognisesSystemCriticalProcesses(string name) =>
        Assert.True(ProcessService.IsCritical(name));

    [Theory]
    [InlineData("explorer")]
    [InlineData("notepad")]
    [InlineData("chrome")]
    public void IsCritical_AllowsOrdinaryProcesses(string name) =>
        Assert.False(ProcessService.IsCritical(name));

    [Fact]
    public void EndProcess_RefusesToEndItself() =>
        Assert.False(new ProcessService().EndProcess(Environment.ProcessId));

    [Fact]
    public void EndProcess_RefusesSystemPids() =>
        Assert.False(new ProcessService().EndProcess(4));
}
