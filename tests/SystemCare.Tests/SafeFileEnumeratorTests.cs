using System.IO;
using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="SafeFileEnumerator"/> is the shared filesystem-walking primitive used by every scanner
/// (junk, duplicates, leftovers): never follow reparse points, never throw on inaccessible entries,
/// and degrade to "found nothing" rather than blow up when the root itself is missing.
/// </summary>
public sealed class SafeFileEnumeratorTests : IDisposable
{
    private readonly string _root;

    public SafeFileEnumeratorTests()
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
    public void RecursiveOptions_NeverFollowsReparsePointsAndIgnoresInaccessible()
    {
        var options = SafeFileEnumerator.RecursiveOptions();

        Assert.True(options.RecurseSubdirectories);
        Assert.True(options.IgnoreInaccessible);
        Assert.Equal(FileAttributes.ReparsePoint, options.AttributesToSkip);
    }

    [Fact]
    public void TopLevelOptions_DoesNotRecurseButStillIgnoresInaccessible()
    {
        var options = SafeFileEnumerator.TopLevelOptions();

        Assert.False(options.RecurseSubdirectories);
        Assert.True(options.IgnoreInaccessible);
        Assert.Equal(FileAttributes.ReparsePoint, options.AttributesToSkip);
    }

    [Fact]
    public void EnumerateFiles_MissingRoot_ReturnsEmpty()
    {
        string missing = Path.Combine(_root, "does-not-exist");

        var files = SafeFileEnumerator.EnumerateFiles(missing).ToList();

        Assert.Empty(files);
    }

    [Fact]
    public void EnumerateFiles_FindsFilesAtTopLevelAndNested()
    {
        File.WriteAllText(Path.Combine(_root, "top.txt"), "a");
        string nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "deep.txt"), "bb");

        var names = SafeFileEnumerator.EnumerateFiles(_root).Select(f => f.Name).OrderBy(n => n).ToList();

        Assert.Equal(["deep.txt", "top.txt"], names);
    }

    [Fact]
    public void EnumerateFiles_EmptyDirectory_ReturnsEmpty()
    {
        var files = SafeFileEnumerator.EnumerateFiles(_root).ToList();

        Assert.Empty(files);
    }

    [Fact]
    public void Measure_MissingRoot_ReturnsZero()
    {
        var (bytes, files) = SafeFileEnumerator.Measure(Path.Combine(_root, "nope"));

        Assert.Equal(0, bytes);
        Assert.Equal(0, files);
    }

    [Fact]
    public void Measure_SumsSizesAndCountsAcrossNestedFiles()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), new string('x', 10));
        string nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "b.txt"), new string('y', 5));

        var (bytes, files) = SafeFileEnumerator.Measure(_root);

        Assert.Equal(15, bytes);
        Assert.Equal(2, files);
    }
}
