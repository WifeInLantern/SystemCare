using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ISoftwareUpdateService
{
    /// <summary>True if winget (App Installer) is present and runnable.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    /// <summary>Lists installed apps with a newer version available (<c>winget upgrade</c>). Never throws.</summary>
    Task<List<SoftwareUpdate>> GetUpgradesAsync(CancellationToken ct);
    /// <summary>Applies the selected upgrades one by one, reporting per-app progress.</summary>
    Task<SoftwareUpdateResult> UpgradeAsync(IEnumerable<SoftwareUpdate> picks, IProgress<SoftwareUpdateProgress>? progress, CancellationToken ct);
}

/// <summary>
/// Updates installed Win32/Store apps via the Windows Package Manager (winget). winget ships with the
/// "App Installer" package; its PATH alias is sometimes missing, so the real executable is resolved from
/// the DesktopAppInstaller package folder. The upgrade list is parsed from winget's fixed-width text table.
/// </summary>
public class SoftwareUpdateService : ISoftwareUpdateService
{
    private string? _wingetPath;
    private bool _resolved;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (ResolveWingetPath() is null) return false;
        var (exit, output) = await RunWingetAsync("--version", ct);
        return exit == 0 && output.TrimStart().StartsWith('v');
    }

    public async Task<List<SoftwareUpdate>> GetUpgradesAsync(CancellationToken ct)
    {
        if (ResolveWingetPath() is null) return [];
        var (_, output) = await RunWingetAsync(
            "upgrade --include-unknown --disable-interactivity --accept-source-agreements", ct);
        return Parse(output);
    }

    public async Task<SoftwareUpdateResult> UpgradeAsync(
        IEnumerable<SoftwareUpdate> picks, IProgress<SoftwareUpdateProgress>? progress, CancellationToken ct)
    {
        var list = picks.ToList();
        if (list.Count == 0) return new SoftwareUpdateResult { Message = "No apps selected." };

        int updated = 0, failed = 0, i = 0;
        foreach (var app in list)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            progress?.Report(new SoftwareUpdateProgress
            {
                Current = i, Total = list.Count, Name = app.Name, Percent = (i - 1) * 100.0 / list.Count,
            });

            var (exit, _) = await RunWingetAsync(
                $"upgrade --id \"{app.Id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                ct);
            if (exit == 0) updated++; else failed++;

            progress?.Report(new SoftwareUpdateProgress
            {
                Current = i, Total = list.Count, Name = app.Name, Percent = i * 100.0 / list.Count,
            });
        }

        string msg = $"Updated {updated} app(s)." + (failed > 0 ? $" {failed} could not be updated." : "");
        return new SoftwareUpdateResult { Updated = updated, Failed = failed, Message = msg };
    }

    // ---------- winget process ----------

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

    private async Task<(int ExitCode, string Output)> RunWingetAsync(string arguments, CancellationToken ct)
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

    // ---------- table parsing ----------

    private static List<SoftwareUpdate> Parse(string output)
    {
        var list = new List<SoftwareUpdate>();
        var lines = output.Replace("\r", "").Split('\n');

        // winget prints source-update progress bars first; the real table starts at the header row.
        int header = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains("Name") && lines[i].Contains("Id") && lines[i].Contains("Available")) { header = i; break; }
        if (header < 0) return list;

        string h = lines[header];
        int cId = h.IndexOf("Id", StringComparison.Ordinal);
        int cVer = h.IndexOf("Version", StringComparison.Ordinal);
        int cAvail = h.IndexOf("Available", StringComparison.Ordinal);
        int cSrc = h.IndexOf("Source", StringComparison.Ordinal);
        if (cId < 0 || cVer < 0 || cAvail < 0) return list;

        for (int i = header + 1; i < lines.Length; i++)
        {
            string l = lines[i];
            if (string.IsNullOrWhiteSpace(l)) continue;
            if (l.StartsWith("---")) continue;
            // footer, e.g. "9 upgrades available." — stops before any secondary "explicit targeting" table.
            if (l.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(l, @"package\(s\) have", RegexOptions.IgnoreCase)) break;

            string id = Slice(l, cId, cVer).Trim();
            string name = Slice(l, 0, cId).Trim();
            if (id.Length == 0 || name.Length == 0) continue;

            list.Add(new SoftwareUpdate
            {
                Name = name,
                Id = id,
                CurrentVersion = Slice(l, cVer, cAvail).Trim(),
                AvailableVersion = Slice(l, cAvail, cSrc < 0 ? l.Length : cSrc).Trim(),
                Source = cSrc < 0 ? "" : Slice(l, cSrc, l.Length).Trim(),
            });
        }
        return list;
    }

    private static string Slice(string s, int start, int end)
    {
        if (start < 0) start = 0;
        if (start >= s.Length) return "";
        if (end > s.Length) end = s.Length;
        return end <= start ? "" : s[start..end];
    }
}
