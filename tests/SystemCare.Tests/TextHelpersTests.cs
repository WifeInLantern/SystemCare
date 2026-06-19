using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="TextHelpers.JoinParts"/> joins non-empty, trimmed parts with " · ", dropping
/// null/blank fields so a detail line never has a leading/trailing/orphan separator.
/// </summary>
public class TextHelpersTests
{
    private const string Sep = " · ";

    [Fact]
    public void JoinParts_AllPresent_JoinsWithSeparator()
    {
        Assert.Equal($"a{Sep}b{Sep}c", TextHelpers.JoinParts("a", "b", "c"));
    }

    [Fact]
    public void JoinParts_DropsNullAndBlankParts()
    {
        Assert.Equal($"a{Sep}b", TextHelpers.JoinParts("a", null, "", "   ", "b"));
    }

    [Fact]
    public void JoinParts_TrimsSurroundingWhitespace()
    {
        Assert.Equal($"a{Sep}b", TextHelpers.JoinParts("  a  ", " b "));
    }

    [Fact]
    public void JoinParts_SinglePart_HasNoSeparator()
    {
        Assert.Equal("only", TextHelpers.JoinParts("only"));
    }

    [Fact]
    public void JoinParts_NoUsableParts_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextHelpers.JoinParts(null, "", "  "));
    }

    [Fact]
    public void JoinParts_NoArguments_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextHelpers.JoinParts());
    }
}
