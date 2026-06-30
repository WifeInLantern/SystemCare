using System.Diagnostics;
using System.Text;

namespace SystemCare.Services;

/// <summary>
/// Locates and runs the Windows Package Manager (winget). Abstracted behind an interface so the
/// software-update service and its table parser can be unit-tested without spawning a real process.
/// winget ships with the "App Installer" package; its PATH alias is sometimes missing, so the real
/// executable is also resolved from the DesktopAppInstaller package folder.
/// </summary>
public interface IWingetRunner
{
    /// <summary>True if a winget executable was located on this machine.</summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Runs winget with the given arguments and returns its exit code and combined stdout+stderr.
    /// Returns <c>(-1, "")</c> when winget is unavailable or the process could not be run. Never throws
    /// except <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled.
    /// </summary>
    Task<(int ExitCode, string Output)> RunAsync(string arguments, CancellationToken ct);
}

public sealed class WingetRunner : IWingetRunner
{
    private string? _wingetPath;
    private bool _resolved;

    public bool IsInstalled => ResolveWingetPath() is not null;

    public async Task<(int ExitCode, string Output)> RunAsync(string arguments, CancellationToken ct)
    {
        string? exe = ResolveWingetPath();
        if (exe is null) return (-1, "");

        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await using var reg = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
            });

            await process.WaitForExitAsync(ct);
            string output = await stdout + await stderr;
            return (process.ExitCode, output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (-1, "");
        }
    }

    private string? ResolveWingetPath()
    {
        if (_resolved) return _wingetPath;
        _resolved = true;

        // 1. App execution alias on PATH (present on most machines).
        try
        {
            string alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(alias)) return _wingetPath = alias;
        }
        catch (Exception) { }

        // 2. The real exe inside the DesktopAppInstaller package (alias can be missing). Admin can read this.
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            string? found = Directory.GetDirectories(root, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                .Select(d => Path.Combine(d, "winget.exe"))
                .Where(File.Exists)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (found is not null) return _wingetPath = found;
        }
        catch (Exception) { }

        return _wingetPath = null; // not installed
    }
}
