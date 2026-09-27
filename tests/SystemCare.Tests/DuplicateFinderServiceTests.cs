using System.IO;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="DuplicateFinderService"/> feeds a delete-the-extra-copies UI, so a false positive can cost the
/// user their only copy of a file. These tests pin down that a file reached through two overlapping search
/// roots is never reported as a duplicate of itself.
/// </summary>
public sealed class DuplicateFinderServiceTests : IDisposable
{
    private readonly string _root;

    public DuplicateFinderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SystemCare.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task FindAsync_OverlappingRoots_DoesNotReportAFileAsItsOwnDuplicate()
    {
        string sub = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "only-copy.txt"), "unique contents");

        var groups = await new DuplicateFinderService().FindAsync([_root, sub], 0, null, CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindAsync_FindsRealDuplicates_AcrossOverlappingRoots()
    {
        string sub = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_root, "a.txt"), "same contents");
        File.WriteAllText(Path.Combine(sub, "b.txt"), "same contents");

        var groups = await new DuplicateFinderService().FindAsync([_root, sub], 0, null, CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
    }
}
