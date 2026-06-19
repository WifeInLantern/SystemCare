using System;
using System.IO;
using SystemCare.Helpers;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="CommandLineParser.ExtractExecutablePath"/> resolves the real executable out of a registry
/// Run command (quoted or unquoted, with or without arguments, with %ENV% expansion).
///
/// It calls <c>File.Exists</c> directly, so it can't be mocked without a refactor — these tests use real
/// temp files with per-test setup/teardown instead. xUnit news up the class per test, so each case gets an
/// isolated temp tree. (Refactor suggestion: inject a <c>Func&lt;string,bool&gt; fileExists</c> to make the
/// file-system probe a true seam.)
/// </summary>
public class CommandLineParserTests : IDisposable
{
    private readonly string _root;
    private readonly string _dir;        // temp dir with no spaces
    private readonly string _spacedDir;  // temp dir whose name contains a space
    private readonly string _exe;        // <_dir>\app.exe          (exists)
    private readonly string _toolExe;    // <_dir>\tool.exe         (exists)
    private readonly string _noExt;      // <_dir>\tool             (resolves via the .exe fallback)
    private readonly string _spacedExe;  // <_spacedDir>\app.exe    (exists)

    public CommandLineParserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sc-clp-" + Guid.NewGuid().ToString("N"));
        _dir = Path.Combine(_root, "nospace");
        _spacedDir = Path.Combine(_root, "with space");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_spacedDir);

        _exe = Path.Combine(_dir, "app.exe");
        _toolExe = Path.Combine(_dir, "tool.exe");
        _noExt = Path.Combine(_dir, "tool");
        _spacedExe = Path.Combine(_spacedDir, "app.exe");
        File.WriteAllText(_exe, "");
        File.WriteAllText(_toolExe, "");
        File.WriteAllText(_spacedExe, "");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractExecutablePath_NullOrBlank_ReturnsNull(string? command)
    {
        Assert.Null(CommandLineParser.ExtractExecutablePath(command));
    }

    [Fact]
    public void ExtractExecutablePath_QuotedExistingPath_ReturnsPath()
    {
        Assert.Equal(_exe, CommandLineParser.ExtractExecutablePath($"\"{_exe}\""));
    }

    [Fact]
    public void ExtractExecutablePath_QuotedPathWithArguments_ReturnsPathOnly()
    {
        Assert.Equal(_exe, CommandLineParser.ExtractExecutablePath($"\"{_exe}\" /silent -x"));
    }

    [Fact]
    public void ExtractExecutablePath_QuotedNonexistentPath_ReturnsNull()
    {
        Assert.Null(CommandLineParser.ExtractExecutablePath($"\"{Path.Combine(_dir, "missing.exe")}\""));
    }

    [Fact]
    public void ExtractExecutablePath_UnterminatedQuote_ReturnsNull()
    {
        Assert.Null(CommandLineParser.ExtractExecutablePath("\"C:\\nope\\app.exe"));
    }

    [Fact]
    public void ExtractExecutablePath_UnquotedExistingPath_ReturnsPath()
    {
        Assert.Equal(_exe, CommandLineParser.ExtractExecutablePath(_exe));
    }

    [Fact]
    public void ExtractExecutablePath_UnquotedPathWithArguments_WalksBackToExecutable()
    {
        Assert.Equal(_exe, CommandLineParser.ExtractExecutablePath($"{_exe} -x /q"));
    }

    [Fact]
    public void ExtractExecutablePath_UnquotedWithoutExtension_AppliesExeFallback()
    {
        Assert.Equal(_toolExe, CommandLineParser.ExtractExecutablePath(_noExt));
    }

    [Fact]
    public void ExtractExecutablePath_UnquotedWithoutExtensionAndArguments_ResolvesToExe()
    {
        Assert.Equal(_toolExe, CommandLineParser.ExtractExecutablePath($"{_noExt} /quiet"));
    }

    [Fact]
    public void ExtractExecutablePath_UnquotedPathContainingSpaces_ResolvesAgainstArguments()
    {
        // The path itself contains a space ("with space"), plus a trailing argument.
        Assert.Equal(_spacedExe, CommandLineParser.ExtractExecutablePath($"{_spacedExe} -arg"));
    }

    [Fact]
    public void ExtractExecutablePath_UnquotedUnresolvable_WalksEveryTokenThenReturnsNull()
    {
        // No prefix of this spaced, unquoted command resolves to a file, so the loop exhausts -> null.
        Assert.Null(CommandLineParser.ExtractExecutablePath("no such file here"));
    }

    [Fact]
    public void ExtractExecutablePath_ExpandsEnvironmentVariables()
    {
        const string var = "SC_CLP_TEST_DIR";
        Environment.SetEnvironmentVariable(var, _dir);
        try
        {
            Assert.Equal(_exe, CommandLineParser.ExtractExecutablePath($"\"%{var}%\\app.exe\""));
        }
        finally
        {
            Environment.SetEnvironmentVariable(var, null);
        }
    }
}
