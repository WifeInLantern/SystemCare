using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="WingetUpgradeParser"/> turns the fixed-width <c>winget upgrade</c> table into update rows.
/// The key regression these tests guard: winget prints a progress spinner using carriage returns on the
/// SAME line as the header. An earlier version stripped <c>\r</c> entirely, which fused the spinner in
/// front of "Name", threw every column offset off, and made the parser silently return zero updates even
/// when updates existed. <see cref="RealWingetUpgradeOutput"/> is captured verbatim (spinner included).
/// </summary>
public class WingetUpgradeParserTests
{
    private const string RealWingetUpgradeOutput = WingetTestSamples.EightUpgradesWithSpinner;

    [Fact]
    public void Parse_RealOutputWithProgressSpinner_ReturnsEveryRow()
    {
        var updates = WingetUpgradeParser.Parse(RealWingetUpgradeOutput);

        // The whole point: 8 updates present -> 8 parsed (regression guard against the spinner bug).
        Assert.Equal(8, updates.Count);
    }

    [Fact]
    public void Parse_SplitsColumnsCorrectly_IncludingNamesWithSpacesAndPlusVersions()
    {
        var updates = WingetUpgradeParser.Parse(RealWingetUpgradeOutput);

        var winrar = updates.Single(u => u.Id == "RARLab.WinRAR");
        Assert.Equal("WinRAR 7.22 (64-bit)", winrar.Name);     // name contains spaces and parens
        Assert.Equal("7.22.0", winrar.CurrentVersion);
        Assert.Equal("7.23.0", winrar.AvailableVersion);
        Assert.Equal("winget", winrar.Source);

        var lmStudio = updates.Single(u => u.Id == "ElementLabs.LMStudio");
        Assert.Equal("0.4.16+2", lmStudio.CurrentVersion);     // version contains '+'
        Assert.Equal("0.4.18+1", lmStudio.AvailableVersion);
    }

    [Fact]
    public void Parse_PreservesUnknownCurrentVersion()
    {
        var updates = WingetUpgradeParser.Parse(RealWingetUpgradeOutput);

        var roblox = updates.Single(u => u.Id == "Roblox.Roblox");
        Assert.Equal("Unknown", roblox.CurrentVersion);
        Assert.Equal("0.726", roblox.AvailableVersion);
    }

    [Fact]
    public void Parse_StopsAtFooter_DoesNotTreatCountLineAsApp()
    {
        var updates = WingetUpgradeParser.Parse(RealWingetUpgradeOutput);

        Assert.DoesNotContain(updates, u => u.Name.Contains("upgrades available"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No installed package found matching input criteria.")]
    public void Parse_NoTable_ReturnsEmpty(string output)
    {
        Assert.Empty(WingetUpgradeParser.Parse(output));
    }

    [Fact]
    public void Parse_HeaderWithNoDataRows_ReturnsEmpty()
    {
        string output =
            "Name   Id   Version   Available   Source\n" +
            "-----------------------------------------\n" +
            "0 upgrades available.\n";

        Assert.Empty(WingetUpgradeParser.Parse(output));
    }
}
