using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="PathExclusionMatcher.IsExcluded"/> decides whether a scanned path falls under a
/// user-configured "don't touch this" exclusion — extracted from <c>JunkScanService</c> so the
/// matching rules can be exercised without a real filesystem scan.
/// </summary>
public class PathExclusionMatcherTests
{
    [Fact]
    public void NoExclusions_NeverExcludes()
    {
        Assert.False(PathExclusionMatcher.IsExcluded(@"C:\Users\me\AppData\Local\Temp\file.tmp", []));
    }

    [Fact]
    public void PathInsideExclusion_IsExcluded()
    {
        var exclusions = new[] { @"C:\Users\me\Important" };

        Assert.True(PathExclusionMatcher.IsExcluded(@"C:\Users\me\Important\notes.txt", exclusions));
    }

    [Fact]
    public void PathEqualToExclusion_IsExcluded()
    {
        var exclusions = new[] { @"C:\Users\me\Important" };

        Assert.True(PathExclusionMatcher.IsExcluded(@"C:\Users\me\Important", exclusions));
    }

    [Fact]
    public void TrailingBackslashOnExclusion_IsNormalized()
    {
        var exclusions = new[] { @"C:\Users\me\Important\" };

        Assert.True(PathExclusionMatcher.IsExcluded(@"C:\Users\me\Important\notes.txt", exclusions));
        Assert.True(PathExclusionMatcher.IsExcluded(@"C:\Users\me\Important", exclusions));
    }

    [Fact]
    public void MatchIsCaseInsensitive()
    {
        var exclusions = new[] { @"C:\Users\me\Important" };

        Assert.True(PathExclusionMatcher.IsExcluded(@"c:\users\me\important\notes.txt", exclusions));
    }

    [Fact]
    public void SiblingWithSharedPrefix_IsNotExcluded()
    {
        // "ImportantBackup" must not be treated as inside "Important".
        var exclusions = new[] { @"C:\Users\me\Important" };

        Assert.False(PathExclusionMatcher.IsExcluded(@"C:\Users\me\ImportantBackup\file.txt", exclusions));
    }

    [Fact]
    public void PathOutsideAllExclusions_IsNotExcluded()
    {
        var exclusions = new[] { @"C:\Users\me\Important", @"D:\Backups" };

        Assert.False(PathExclusionMatcher.IsExcluded(@"C:\Users\me\AppData\Local\Temp\file.tmp", exclusions));
    }

    [Fact]
    public void BlankAndWhitespaceExclusions_AreIgnored()
    {
        var exclusions = new[] { "", "   ", @"C:\Users\me\Important" };

        Assert.False(PathExclusionMatcher.IsExcluded(@"C:\Users\other\file.txt", exclusions));
        Assert.True(PathExclusionMatcher.IsExcluded(@"C:\Users\me\Important\notes.txt", exclusions));
    }
}
