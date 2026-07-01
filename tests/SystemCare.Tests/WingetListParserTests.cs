using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="WingetListParser"/> extracts installed package IDs from <c>winget list</c> output. Unlike
/// <see cref="WingetUpgradeParser"/> it must tolerate a blank/absent "Available" column (nothing to
/// upgrade to) and rows with no resolvable Id (ARP-only entries) — <see cref="WingetTestSamples.InstalledAppsListSample"/>
/// includes both.
/// </summary>
public class WingetListParserTests
{
    [Fact]
    public void ParseInstalledIds_RealOutput_ReturnsAllResolvableIds()
    {
        var ids = WingetListParser.ParseInstalledIds(WingetTestSamples.InstalledAppsListSample);

        Assert.Contains("7zip.7zip", ids);
        Assert.Contains("Git.Git", ids);
        Assert.Contains("VideoLAN.VLC", ids);
        Assert.Contains("RARLab.WinRAR", ids);
        Assert.Contains("Microsoft.VCRedist.2015+.x64", ids);
    }

    [Fact]
    public void ParseInstalledIds_BlankIdRow_IsSkipped_NotCountedOrCrashing()
    {
        var ids = WingetListParser.ParseInstalledIds(WingetTestSamples.InstalledAppsListSample);

        Assert.DoesNotContain(ids, id => id.Length == 0);
        // 6 rows in the sample, one (Realtek Audio Console) has a blank Id.
        Assert.Equal(5, ids.Count);
    }

    [Fact]
    public void ParseInstalledIds_IsCaseInsensitive()
    {
        var ids = WingetListParser.ParseInstalledIds(WingetTestSamples.InstalledAppsListSample);

        Assert.Contains("git.git", ids);
        Assert.Contains("VIDEOLAN.VLC", ids);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No installed package found matching input criteria.")]
    public void ParseInstalledIds_NoTable_ReturnsEmpty(string output)
    {
        Assert.Empty(WingetListParser.ParseInstalledIds(output));
    }

    [Fact]
    public void ParseInstalledIds_KnownAppNotInSample_IsNotFlaggedInstalled()
    {
        var ids = WingetListParser.ParseInstalledIds(WingetTestSamples.InstalledAppsListSample);

        Assert.DoesNotContain("Mozilla.Firefox", ids);
    }
}
