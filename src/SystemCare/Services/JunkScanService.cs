using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Native;

namespace SystemCare.Services;

public interface IJunkScanService
{
    IReadOnlyList<JunkCategory> Categories { get; }

    /// <summary>Dry-run: finds and sizes junk, deletes nothing.</summary>
    Task<JunkScanResult> ScanAsync(IEnumerable<string> categoryIds, IProgress<ScanProgress>? progress, CancellationToken ct);

    /// <summary>Deletes the files captured by a previous scan for the selected categories. Files in use are skipped.</summary>
    Task<CleanResult> CleanAsync(JunkScanResult scan, IEnumerable<string> categoryIds, IProgress<ScanProgress>? progress, CancellationToken ct);
}

public class JunkScanService(ISettingsService settings) : IJunkScanService
{
    private static string Local(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);

    private static string Windows(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.Windows), .. parts]);

    private static string ProgramData(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), .. parts]);

    /// <summary>Chromium-style profile cache dirs (Chrome/Edge share the layout).</summary>
    private static IEnumerable<string> ChromiumCacheDirs(string userDataRoot)
    {
        if (!Directory.Exists(userDataRoot)) yield break;
        IEnumerable<string> profiles;
        try
        {
            profiles = Directory.EnumerateDirectories(userDataRoot, "*", SafeFileEnumerator.TopLevelOptions())
                .Where(d =>
                {
                    string name = Path.GetFileName(d);
                    return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
                })
                .ToList();
        }
        catch (Exception)
        {
            yield break;
        }
        foreach (var profile in profiles)
        {
            yield return Path.Combine(profile, "Cache", "Cache_Data");
            yield return Path.Combine(profile, "Code Cache");
            yield return Path.Combine(profile, "GPUCache");
        }
    }

    private static IEnumerable<string> FirefoxCacheDirs()
    {
        string root = Local("Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(root)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(root))
            yield return Path.Combine(profile, "cache2");
    }

    public IReadOnlyList<JunkCategory> Categories { get; } =
    [
        new JunkCategory
        {
            Id = "temp-user", Name = "User temporary files",
            Description = "Files in your user temp folder older than a day",
            ApplyAgeFilter = true,
            GetRoots = () => [Path.GetTempPath()],
        },
        new JunkCategory
        {
            Id = "temp-windows", Name = "Windows temporary files",
            Description = "Files in the system-wide Windows\\Temp folder",
            ApplyAgeFilter = true,
            GetRoots = () => [Windows("Temp")],
        },
        new JunkCategory
        {
            Id = "wu-cache", Name = "Windows Update cache",
            Description = "Downloaded update installers Windows no longer needs",
            GetRoots = () => [Windows("SoftwareDistribution", "Download")],
        },
        new JunkCategory
        {
            Id = "thumb-cache", Name = "Thumbnail & icon cache",
            Description = "Explorer thumbnail caches (rebuilt automatically)",
            GetRoots = () => [Local("Microsoft", "Windows", "Explorer")],
        },
        new JunkCategory
        {
            Id = "wer", Name = "Windows error reports",
            Description = "Queued and archived crash reports",
            GetRoots = () =>
            [
                Local("Microsoft", "Windows", "WER", "ReportArchive"),
                Local("Microsoft", "Windows", "WER", "ReportQueue"),
                ProgramData("Microsoft", "Windows", "WER", "ReportArchive"),
                ProgramData("Microsoft", "Windows", "WER", "ReportQueue"),
            ],
        },
        new JunkCategory
        {
            Id = "crash-dumps", Name = "Crash dumps",
            Description = "Memory dump files from application and system crashes",
            GetRoots = () => [Local("CrashDumps"), Windows("Minidump")],
        },
        new JunkCategory
        {
            Id = "browser-cache-chrome", Name = "Chrome cache",
            Description = "Google Chrome web caches (all profiles)",
            GetRoots = () => ChromiumCacheDirs(Local("Google", "Chrome", "User Data")),
        },
        new JunkCategory
        {
            Id = "browser-cache-edge", Name = "Edge cache",
            Description = "Microsoft Edge web caches (all profiles)",
            GetRoots = () => ChromiumCacheDirs(Local("Microsoft", "Edge", "User Data")),
        },
        new JunkCategory
        {
            Id = "browser-cache-firefox", Name = "Firefox cache",
            Description = "Mozilla Firefox web caches (all profiles)",
            GetRoots = FirefoxCacheDirs,
        },
        new JunkCategory
        {
            Id = "custom-folders", Name = "Custom folders",
            Description = "Old files in folders you added in Settings",
            ApplyAgeFilter = true,
            GetRoots = () => settings.Current.CustomJunkFolders,
        },
        new JunkCategory
        {
            Id = "recycle-bin", Name = "Recycle Bin",
            Description = "Permanently removes everything in the Recycle Bin",
            IsRecycleBin = true,
        },
    ];

    /// <summary>True when a path sits inside any user-configured exclusion.</summary>
    private bool IsExcluded(string fullPath) =>
        PathExclusionMatcher.IsExcluded(fullPath, settings.Current.CleanupExclusions);

    public Task<JunkScanResult> ScanAsync(
        IEnumerable<string> categoryIds, IProgress<ScanProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var wanted = categoryIds.ToHashSet();
        var result = new JunkScanResult();
        long totalBytes = 0;
        int totalFiles = 0;
        var lastReport = DateTime.MinValue;

        foreach (var category in Categories.Where(c => wanted.Contains(c.Id)))
        {
            ct.ThrowIfCancellationRequested();
            var categoryResult = new JunkCategoryResult { Category = category };

            if (category.IsRecycleBin)
            {
                var (bytes, items) = NativeMethods.QueryRecycleBin();
                categoryResult.TotalBytes = bytes;
                categoryResult.FileCount = (int)Math.Min(items, int.MaxValue);
            }
            else
            {
                DateTime cutoffUtc = DateTime.UtcNow.AddHours(-settings.Current.SkipTempNewerThanHours);
                foreach (var root in category.GetRoots())
                {
                    ct.ThrowIfCancellationRequested();
                    bool thumbCache = category.Id == "thumb-cache";

                    foreach (var file in SafeFileEnumerator.EnumerateFiles(root))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            // Explorer dir doubles as a working dir — only the cache DBs are junk.
                            if (thumbCache &&
                                !(file.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) ||
                                  file.Name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase)))
                                continue;

                            if (category.ApplyAgeFilter && file.LastWriteTimeUtc > cutoffUtc)
                                continue;

                            if (IsExcluded(file.FullName))
                                continue;

                            categoryResult.Items.Add(new JunkItem { Path = file.FullName, Bytes = file.Length });
                            categoryResult.TotalBytes += file.Length;
                            categoryResult.FileCount++;
                            totalBytes += file.Length;
                            totalFiles++;

                            if (progress is not null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 100)
                            {
                                lastReport = DateTime.UtcNow;
                                progress.Report(new ScanProgress
                                {
                                    CurrentPath = file.FullName,
                                    BytesFound = totalBytes,
                                    FilesFound = totalFiles,
                                });
                            }
                        }
                        catch (Exception)
                        {
                            // file vanished mid-scan — skip
                        }
                    }
                }

                // MEMORY.DMP is a single file outside the directory roots.
                if (category.Id == "crash-dumps")
                {
                    string memoryDmp = Windows("MEMORY.DMP");
                    try
                    {
                        if (File.Exists(memoryDmp))
                        {
                            var info = new FileInfo(memoryDmp);
                            categoryResult.Items.Add(new JunkItem { Path = info.FullName, Bytes = info.Length });
                            categoryResult.TotalBytes += info.Length;
                            categoryResult.FileCount++;
                        }
                    }
                    catch (Exception) { }
                }
            }

            result.Categories.Add(categoryResult);
        }

        progress?.Report(new ScanProgress { CurrentPath = "", BytesFound = totalBytes, FilesFound = totalFiles });
        return result;
    }, ct);

    public Task<CleanResult> CleanAsync(
        JunkScanResult scan, IEnumerable<string> categoryIds, IProgress<ScanProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var wanted = categoryIds.ToHashSet();
        var result = new CleanResult();
        var lastReport = DateTime.MinValue;

        foreach (var categoryResult in scan.Categories.Where(c => wanted.Contains(c.Category.Id)))
        {
            ct.ThrowIfCancellationRequested();

            if (categoryResult.Category.IsRecycleBin)
            {
                var (bytes, items) = NativeMethods.QueryRecycleBin();
                if (items > 0)
                {
                    int hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                        NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
                    if (hr == 0)
                    {
                        result.BytesRemoved += bytes;
                        result.FilesRemoved += (int)Math.Min(items, int.MaxValue);
                    }
                }
                continue;
            }

            var touchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in categoryResult.Items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    File.Delete(item.Path);
                    result.BytesRemoved += item.Bytes;
                    result.FilesRemoved++;
                    string? dir = Path.GetDirectoryName(item.Path);
                    if (dir is not null) touchedDirs.Add(dir);
                }
                catch (IOException) { result.FilesSkipped++; }
                catch (UnauthorizedAccessException) { result.FilesSkipped++; }
                catch (Exception) { result.FilesSkipped++; }

                if (progress is not null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 100)
                {
                    lastReport = DateTime.UtcNow;
                    progress.Report(new ScanProgress
                    {
                        CurrentPath = item.Path,
                        BytesFound = result.BytesRemoved,
                        FilesFound = result.FilesRemoved,
                    });
                }
            }

            // Remove now-empty subdirectories (deepest first), but never the scan roots themselves.
            var roots = categoryResult.Category.GetRoots()
                .Select(r => r.TrimEnd('\\'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in touchedDirs.OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!roots.Contains(dir.TrimEnd('\\')) &&
                        Directory.Exists(dir) &&
                        !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch (Exception) { }
            }
        }

        return result;
    }, ct);
}
