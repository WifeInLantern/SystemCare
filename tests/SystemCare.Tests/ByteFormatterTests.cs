using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="ByteFormatter.Format"/> scales bytes up the B/KB/MB/GB/TB ladder (1024-based),
/// shows no decimals for raw bytes and one decimal otherwise, treats negatives as "0 B",
/// and never climbs past TB.
/// </summary>
public class ByteFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]                 // boundary just below 1 KB, no decimal
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]                 // one decimal place
    [InlineData(2560, "2.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    [InlineData(1649267441664, "1.5 TB")]
    public void Format_ScalesToReadableUnit(long bytes, string expected)
    {
        Assert.Equal(expected, ByteFormatter.Format(bytes));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1048576)]
    [InlineData(long.MinValue)]
    public void Format_NegativeBytes_ReturnsZero(long bytes)
    {
        Assert.Equal("0 B", ByteFormatter.Format(bytes));
    }

    [Fact]
    public void Format_BeyondTerabyte_StaysInTerabytes()
    {
        // 1 PiB worth of bytes still reports in TB (the ladder tops out at TB).
        Assert.Equal("1024 TB", ByteFormatter.Format(1024L * 1024 * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Format_MaxValue_DoesNotOverflowAndStaysInTerabytes()
    {
        Assert.EndsWith(" TB", ByteFormatter.Format(long.MaxValue));
    }
}
