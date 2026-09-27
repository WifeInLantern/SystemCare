using System.IO;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// Shredding is irreversible and the app runs elevated, so <see cref="FileShredderService.IsProtectedPath"/>
/// must refuse drive roots, OS folders and profile containers while still allowing ordinary user files.
/// </summary>
public sealed class FileShredderServiceTests : IDisposable
{
    private readonly string _root;

    public FileShredderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SystemCare.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* best-effort cleanup */ }
    }

    public static TheoryData<string> ProtectedLocations()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new TheoryData<string>
        {
            Path.GetPathRoot(windows)!,
            windows,
            Path.Combine(windows, "System32"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            profile,
            Path.GetDirectoryName(profile)!,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };
    }

    [Theory]
    [MemberData(nameof(ProtectedLocations))]
    public void IsProtectedPath_RefusesSystemAndProfileLocations(string path) =>
        Assert.True(FileShredderService.IsProtectedPath(path));

    [Fact]
    public void IsProtectedPath_AllowsOrdinaryUserFolder() =>
        Assert.False(FileShredderService.IsProtectedPath(_root));

    [Fact]
    public async Task ShredAsync_RemovesTheFolderAndItsEmptySubfolders()
    {
        string target = Path.Combine(_root, "secret");
        string nested = Path.Combine(target, "a", "b");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "x.txt"), "data");

        var result = await new FileShredderService().ShredAsync([target], 1, null, CancellationToken.None);

        Assert.Equal(1, result.FilesShredded);
        Assert.False(Directory.Exists(target));
    }
}
