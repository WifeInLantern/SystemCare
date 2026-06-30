using System.IO;
using Microsoft.Win32;
using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// Safety-critical coverage for <see cref="LeftoverScanService"/> — the per-app uninstall-leftover finder
/// and remover. These tests pin down the behaviour that keeps it from ever deleting the wrong thing:
/// conservative token matching (a distinctive 4+ char, non-generic token is required), the
/// <c>AcceptFolder</c> guards (drive roots, protected system roots, and folders shared with other installed
/// apps are rejected), and that removal routes files to the Recycle Bin and registry keys through the
/// backed-up registry-clean pipeline. Coined app names are used so the read-only capture scan of the real
/// machine can't accidentally match (or be matched by) genuine folders.
/// </summary>
public class LeftoverScanServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly IFileOperationService _fileOps = Substitute.For<IFileOperationService>();
    private readonly IRegistryCleanerService _registry = Substitute.For<IRegistryCleanerService>();
    private readonly IInstalledAppsService _apps = Substitute.For<IInstalledAppsService>();

    public LeftoverScanServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "sc_leftover_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _apps.GetInstalledAppsAsync().Returns(Task.FromResult(new List<InstalledApp>()));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private LeftoverScanService NewService() => new(_fileOps, _registry, _apps);

    private static InstalledApp App(string name, string publisher = "", string? installLocation = null) => new()
    {
        Name = name,
        Publisher = publisher,
        InstallLocation = installLocation,
        UninstallString = "x",
    };

    private static string Norm(string p) => p.Trim().Trim('"').TrimEnd('\\');

    private static bool HasFolder(LeftoverPlan plan, string path) =>
        plan.Candidates.Any(c => c.Kind == LeftoverKind.Folder &&
                                 string.Equals(Norm(c.Path ?? ""), Norm(path), StringComparison.OrdinalIgnoreCase));

    // ---------- capture: install folder + token matching ----------

    [Fact]
    public void CaptureCandidates_AddsInstallFolder()
    {
        string install = Path.Combine(_tempRoot, "Zorptastic", "bin");
        var plan = NewService().CaptureCandidates(App("Zorptastic", installLocation: install));

        Assert.True(HasFolder(plan, install));
    }

    [Fact]
    public void CaptureCandidates_MatchesVendorParent_OnDistinctiveToken()
    {
        // Parent folder named with the app's distinctive token ("Zorptastic") is flagged as the program folder.
        string parent = Path.Combine(_tempRoot, "Zorptastic");
        string install = Path.Combine(parent, "bin");
        var plan = NewService().CaptureCandidates(App("Zorptastic", installLocation: install));

        Assert.True(HasFolder(plan, parent));
    }

    [Fact]
    public void CaptureCandidates_DoesNotMatchGenericParent()
    {
        // "Media Player" has no distinctive token (both words are in the generic blocklist), so the parent
        // folder "Media" must NOT be flagged — only the literal install folder is.
        string parent = Path.Combine(_tempRoot, "Media");
        string install = Path.Combine(parent, "bin");
        var plan = NewService().CaptureCandidates(App("Media Player", installLocation: install));

        Assert.True(HasFolder(plan, install));   // install folder always captured
        Assert.False(HasFolder(plan, parent));   // generic-named parent is not
    }

    // ---------- capture: AcceptFolder safety guards ----------

    [Fact]
    public void CaptureCandidates_RejectsDriveRootInstallLocation()
    {
        var plan = NewService().CaptureCandidates(App("Zorptastic", installLocation: @"C:\"));

        Assert.DoesNotContain(plan.Candidates, c => c.Kind == LeftoverKind.Folder && Norm(c.Path ?? "") == "C:");
    }

    [Fact]
    public void CaptureCandidates_RejectsProtectedSystemRoot()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var plan = NewService().CaptureCandidates(App("Zorptastic", installLocation: windows));

        Assert.False(HasFolder(plan, windows));
    }

    [Fact]
    public void CaptureCandidates_RejectsProgramFilesRoot()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var plan = NewService().CaptureCandidates(App("Zorptastic", installLocation: pf));

        Assert.False(HasFolder(plan, pf));
    }

    [Fact]
    public void CaptureCandidates_RejectsFolderSharedWithAnotherInstalledApp()
    {
        // Another app lives at <root>\Shared; the app under test installs into <root>\Shared\Sub — a
        // descendant of another app's folder must never be flagged for removal.
        string shared = Path.Combine(_tempRoot, "Shared");
        string install = Path.Combine(shared, "Sub");
        _apps.GetInstalledAppsAsync().Returns(Task.FromResult(new List<InstalledApp>
        {
            App("Other App", installLocation: shared),
        }));

        var plan = NewService().CaptureCandidates(App("Zorptastic", installLocation: install));

        Assert.DoesNotContain(plan.Candidates,
            c => c.Kind == LeftoverKind.Folder && Norm(c.Path ?? "").StartsWith(Norm(shared), StringComparison.OrdinalIgnoreCase));
    }

    // ---------- verify ----------

    [Fact]
    public async Task VerifyAsync_ReturnsOnlySurvivors_AndMeasuresSize()
    {
        string existing = Path.Combine(_tempRoot, "Survives");
        Directory.CreateDirectory(existing);
        File.WriteAllBytes(Path.Combine(existing, "data.bin"), new byte[1234]);
        string missing = Path.Combine(_tempRoot, "GoneAlready");

        var plan = new LeftoverPlan
        {
            App = App("Zorptastic"),
            Candidates =
            {
                new LeftoverItem { Kind = LeftoverKind.Folder, Path = existing, Reason = "Install folder" },
                new LeftoverItem { Kind = LeftoverKind.Folder, Path = missing, Reason = "AppData folder" },
            },
        };

        var survivors = await NewService().VerifyAsync(plan, CancellationToken.None);

        Assert.Single(survivors);
        Assert.Equal(existing, survivors[0].Path);
        Assert.Equal(1234, survivors[0].SizeBytes);
    }

    // ---------- removal ----------

    private static LeftoverItem Folder(string path, long size) =>
        new() { Kind = LeftoverKind.Folder, Path = path, SizeBytes = size };

    [Fact]
    public async Task RemoveAsync_SendsFilesToRecycleBin_AndCountsBytes()
    {
        _fileOps.SendToRecycleBin(Arg.Any<string>()).Returns(true);
        var items = new[] { Folder(@"C:\x\AppOne", 100), Folder(@"C:\x\AppTwo", 250) };

        var result = await NewService().RemoveAsync(items, null, CancellationToken.None);

        Assert.Equal(2, result.FilesRemoved);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(350, result.BytesRemoved);
        _fileOps.Received(1).SendToRecycleBin(@"C:\x\AppOne");
        _fileOps.Received(1).SendToRecycleBin(@"C:\x\AppTwo");
    }

    [Fact]
    public async Task RemoveAsync_FailedDeletion_CountsAsSkipped()
    {
        _fileOps.SendToRecycleBin(@"C:\x\Good").Returns(true);
        _fileOps.SendToRecycleBin(@"C:\x\Locked").Returns(false);
        var items = new[] { Folder(@"C:\x\Good", 100), Folder(@"C:\x\Locked", 100) };

        var result = await NewService().RemoveAsync(items, null, CancellationToken.None);

        Assert.Equal(1, result.FilesRemoved);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(100, result.BytesRemoved);
    }

    [Fact]
    public async Task RemoveAsync_RoutesRegistryItemsThroughBackedUpCleaner_NotRecycleBin()
    {
        _registry.CleanAsync(Arg.Any<IEnumerable<RegistryIssue>>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(new RegistryCleanResult { Removed = 1, Skipped = 0, BackupFolder = @"C:\backups\2026" });
        var items = new[]
        {
            new LeftoverItem { Kind = LeftoverKind.RegistryKey, Hive = RegistryHive.CurrentUser, SubKeyPath = @"Software\Zorptastic" },
        };

        var result = await NewService().RemoveAsync(items, null, CancellationToken.None);

        Assert.Equal(1, result.RegistryRemoved);
        Assert.Equal(@"C:\backups\2026", result.RegistryBackupFolder);
        await _registry.Received(1).CleanAsync(Arg.Any<IEnumerable<RegistryIssue>>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
        _fileOps.DidNotReceiveWithAnyArgs().SendToRecycleBin(default!);
    }

    [Fact]
    public async Task RemoveAsync_Cancelled_Throws()
    {
        _fileOps.SendToRecycleBin(Arg.Any<string>()).Returns(true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewService().RemoveAsync(new[] { Folder(@"C:\x\AppOne", 100) }, null, cts.Token));
    }
}
