using System.IO;
using SystemCare.Helpers;

namespace SystemCare.Services;

/// <summary>One cleanable cache target: a named app/tool cache made of one or more folders.</summary>
public sealed class AppCacheTarget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>"Apps" or "Developer".</summary>
    public required string Group { get; init; }
    public required string Description { get; init; }
    /// <summary>Absolute folders (environment variables already expanded). Missing folders are skipped.</summary>
    public required IReadOnlyList<string> Folders { get; init; }
}

public sealed class AppCacheScanResult
{
    public required AppCacheTarget Target { get; init; }
    public long Bytes { get; init; }
    public int Files { get; init; }
}

public sealed class AppCacheCleanResult
{
    public long BytesRemoved { get; init; }
    public int FilesRemoved { get; init; }
    public int FilesSkipped { get; init; }
}

public interface IAppCacheService
{
    /// <summary>Targets whose folders exist on this machine (apps not installed are filtered out).</summary>
    IReadOnlyList<AppCacheTarget> GetAvailableTargets();

    /// <summary>Dry-run: measures each target. Never deletes.</summary>
    Task<IReadOnlyList<AppCacheScanResult>> ScanAsync(IEnumerable<AppCacheTarget> targets, CancellationToken ct);

    /// <summary>Deletes cache files for the given targets. Files newer than 24h and in-use files
    /// are skipped (caches being written right now are left alone). Only whitelisted catalog
    /// folders are ever touched; junctions/symlinks are never followed.</summary>
    Task<AppCacheCleanResult> CleanAsync(IEnumerable<AppCacheTarget> targets, CancellationToken ct);
}

/// <summary>
/// App Cache Cleaner (2.14): a curated, whitelisted catalog of per-app caches that are safe to
/// purge — apps rebuild them on next launch. Same discipline as Junk Cleanup: dry-run scans,
/// recently-written files protected, per-file try/catch so an in-use file never aborts a clean.
/// Deletes are permanent (these are regenerable caches, often multi-GB — recycling them would
/// defeat the purpose); the catalog deliberately contains no user data, settings, or documents.
/// </summary>
public sealed class AppCacheService(IHistoryService history, ILogService log) : IAppCacheService
{
    private static readonly TimeSpan RecentFileProtection = TimeSpan.FromHours(24);

    private static string Local(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);
    private static string Roaming(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), .. parts]);
    private static string Profile(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), .. parts]);

    // The whitelist. Every folder here is a regenerable cache; nothing user-authored.
    private static List<AppCacheTarget> BuildCatalog() =>
    [
        new()
        {
            Id = "discord", Name = "Discord", Group = "Apps",
            Description = "Message-media and web caches; rebuilt on next launch. Close Discord first for a full clean.",
            Folders = [Roaming("discord", "Cache"), Roaming("discord", "Code Cache"), Roaming("discord", "GPUCache")],
        },
        new()
        {
            Id = "slack", Name = "Slack", Group = "Apps",
            Description = "Web and media caches; rebuilt on next launch. Close Slack first for a full clean.",
            Folders = [Roaming("Slack", "Cache"), Roaming("Slack", "Code Cache"), Roaming("Slack", "GPUCache")],
        },
        new()
        {
            Id = "teams", Name = "Microsoft Teams (classic)", Group = "Apps",
            Description = "Web caches of the classic Teams desktop app; rebuilt on next launch.",
            Folders = [Roaming("Microsoft", "Teams", "Cache"), Roaming("Microsoft", "Teams", "Code Cache"), Roaming("Microsoft", "Teams", "GPUCache")],
        },
        new()
        {
            Id = "spotify", Name = "Spotify", Group = "Apps",
            Description = "Streamed-audio cache; songs re-buffer on demand. Downloaded playlists are not stored here.",
            Folders = [Local("Spotify", "Storage"), Local("Spotify", "Data")],
        },
        new()
        {
            Id = "nvidia-shaders", Name = "NVIDIA shader caches", Group = "Apps",
            Description = "Compiled shader caches (DXCache/GLCache); games rebuild them on first launch, briefly.",
            Folders = [Local("NVIDIA", "DXCache"), Local("NVIDIA", "GLCache")],
        },
        new()
        {
            Id = "steam-shaders", Name = "Steam shader cache", Group = "Apps",
            Description = "Pre-compiled GPU shaders per game; re-downloaded/rebuilt automatically by Steam.",
            Folders = [@"C:\Program Files (x86)\Steam\steamapps\shadercache"],
        },
        new()
        {
            Id = "npm", Name = "npm cache", Group = "Developer",
            Description = "Package tarball cache (npm-cache); packages re-download on next install.",
            Folders = [Local("npm-cache")],
        },
        new()
        {
            Id = "yarn", Name = "Yarn cache", Group = "Developer",
            Description = "Global Yarn package cache; packages re-download on next install.",
            Folders = [Local("Yarn", "Cache")],
        },
        new()
        {
            Id = "pip", Name = "pip cache", Group = "Developer",
            Description = "Python wheel/HTTP cache; packages re-download on next install.",
            Folders = [Local("pip", "cache")],
        },
        new()
        {
            Id = "nuget", Name = "NuGet HTTP cache", Group = "Developer",
            Description = "HTTP/v3 metadata caches only — the global packages folder is NOT touched.",
            Folders = [Local("NuGet", "http-cache"), Local("NuGet", "v3-cache")],
        },
        new()
        {
            Id = "gradle", Name = "Gradle caches", Group = "Developer",
            Description = "Build and dependency caches under ~/.gradle/caches; re-downloaded on next build (can be slow once).",
            Folders = [Profile(".gradle", "caches")],
        },
    ];

    public IReadOnlyList<AppCacheTarget> GetAvailableTargets() =>
        BuildCatalog().Where(t => t.Folders.Any(Directory.Exists)).ToList();

    public Task<IReadOnlyList<AppCacheScanResult>> ScanAsync(IEnumerable<AppCacheTarget> targets, CancellationToken ct) =>
        Task.Run<IReadOnlyList<AppCacheScanResult>>(() =>
        {
            var results = new List<AppCacheScanResult>();
            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                long bytes = 0; int files = 0;
                foreach (var folder in target.Folders.Where(Directory.Exists))
                {
                    var (b, f) = SafeFileEnumerator.Measure(folder);
                    bytes += b; files += f;
                }
                results.Add(new AppCacheScanResult { Target = target, Bytes = bytes, Files = files });
            }
            return results;
        }, ct);

    public Task<AppCacheCleanResult> CleanAsync(IEnumerable<AppCacheTarget> targets, CancellationToken ct) =>
        Task.Run(() =>
        {
            long bytesRemoved = 0; int removed = 0, skipped = 0;
            var cutoff = DateTime.UtcNow - RecentFileProtection;

            foreach (var target in targets)
            {
                foreach (var folder in target.Folders.Where(Directory.Exists))
                {
                    foreach (var file in SafeFileEnumerator.EnumerateFiles(folder))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            if (file.LastWriteTimeUtc > cutoff) { skipped++; continue; } // likely in active use
                            long size = file.Length;
                            file.Delete();
                            bytesRemoved += size;
                            removed++;
                        }
                        catch (Exception)
                        {
                            skipped++; // locked/in-use — caches being written right now are left alone
                        }
                    }
                }
            }

            if (removed > 0)
            {
                history.Record("App Caches", $"Cleaned app caches: {removed:N0} files", bytesFreed: bytesRemoved,
                    itemCount: removed, icon: "Layer24");
                log.Info("AppCaches", $"Removed {removed} files ({ByteFormatter.Format(bytesRemoved)}), skipped {skipped}.");
            }
            return new AppCacheCleanResult { BytesRemoved = bytesRemoved, FilesRemoved = removed, FilesSkipped = skipped };
        }, ct);
}
