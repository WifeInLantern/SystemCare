using NSubstitute;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="WingetSearchParser"/> parses <c>winget search</c> tables: it must survive the
/// carriage-return spinner fused onto the header line (split on \r, never strip it), handle the
/// optional "Match" column that appears between Version and Source for tag/moniker matches, skip
/// rows with blank or ellipsis-truncated Ids (uninstallable with <c>--exact</c>), and return empty
/// for the "No package found" message.
/// </summary>
public class WingetSearchParserTests
{
    [Fact]
    public void Parse_SpinnerOnHeaderLine_StillFindsHeaderAndRows()
    {
        var results = WingetSearchParser.Parse(WingetTestSamples.SearchResultsWithSpinnerAndMatch);

        Assert.Equal(3, results.Count); // 5 rows minus blank-Id and truncated-Id
        Assert.Equal("Visual Studio Code", results[0].Name);
        Assert.Equal("Microsoft.VisualStudioCode", results[0].Id);
        Assert.Equal("1.90.0", results[0].Version);
        Assert.Equal("winget", results[0].Source);
    }

    [Fact]
    public void Parse_MatchColumn_DoesNotLeakIntoVersionOrSource()
    {
        var results = WingetSearchParser.Parse(WingetTestSamples.SearchResultsWithSpinnerAndMatch);

        var npp = results.Single(r => r.Id == "Notepad++.Notepad++");
        Assert.Equal("8.6.9", npp.Version);       // must not be "8.6.9      Tag: editor"
        Assert.Equal("winget", npp.Source);

        var vlc = results.Single(r => r.Id == "VideoLAN.VLC");
        Assert.Equal("3.0.20", vlc.Version);
    }

    [Fact]
    public void Parse_NoMatchColumn_VersionEndsAtSource()
    {
        var results = WingetSearchParser.Parse(WingetTestSamples.SearchResultsWithoutMatch);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("winget", r.Source));
        Assert.Equal("1.90.0", results.Single(r => r.Id == "Microsoft.VisualStudioCode").Version);
    }

    [Fact]
    public void Parse_TruncatedId_IsSkipped()
    {
        var results = WingetSearchParser.Parse(WingetTestSamples.SearchResultsWithSpinnerAndMatch);
        Assert.DoesNotContain(results, r => r.Id.Contains('…'));
    }

    [Fact]
    public void Parse_BlankId_IsSkipped()
    {
        var results = WingetSearchParser.Parse(WingetTestSamples.SearchResultsWithSpinnerAndMatch);
        Assert.DoesNotContain(results, r => r.Name == "Orphaned ARP Entry");
    }

    [Theory]
    [InlineData("")]
    [InlineData(WingetTestSamples.SearchNoResults)]
    public void Parse_NoTable_ReturnsEmpty(string output)
    {
        Assert.Empty(WingetSearchParser.Parse(output));
    }

    [Fact]
    public void Parse_TableWithNoParsableRows_LogsFormatDriftWarning()
    {
        var log = Substitute.For<ILogService>();
        // A separator line marks "winget clearly printed a table", but the localised header is unrecognisable.
        WingetSearchParser.Parse("Nom    Identifiant    Version\n----------\nFoo    Foo.Foo    1.0\n", log);

        log.Received(1).Warn("SoftwareHub", Arg.Is<string>(m => m.Contains("format/locale drift")));
    }
}
