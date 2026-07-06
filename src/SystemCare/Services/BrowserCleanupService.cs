using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IBrowserCleanupService
{
    /// <summary>Detects installed browsers and measures each one's cache size.</summary>
    Task<List<BrowserInfo>> DetectAsync(CancellationToken ct);
    /// <summary>Clears the selected data types for a browser; returns bytes freed. Skips locked files.</summary>
    Task<long> ClearAsync(BrowserInfo browser, bool cache, bool cookies, bool history, CancellationToken ct);
}

public class BrowserCleanupService : IBrowserCleanupService
{
    private readonly ILogService _log;
    public BrowserCleanupService(ILogService log) => _log = log;

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public Task<List<BrowserInfo>> DetectAsync(CancellationToken ct) => Task.Run(() =>
    {
        var candidates = new List<BrowserInfo>
        {
            new() { Name = "Google Chrome", Kind = "Chromium", UserDataPath = Path.Combine(Local, "Google", "Chrome", "User Data") },
            new() { Name = "Microsoft Edge", Kind = "Chromium", UserDataPath = Path.Combine(Local, "Microsoft", "Edge", "User Data") },
            new() { Name = "Brave", Kind = "Chromium", UserDataPath = Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data") },
            new() { Name = "Mozilla Firefox", Kind = "Firefox",
                    UserDataPath = Path.Combine(Roaming, "Mozilla", "Firefox", "Profiles"),
                    LocalDataPath = Path.Combine(Local, "Mozilla", "Firefox", "Profiles") },
        };

        var found = new List<BrowserInfo>();
        foreach (var b in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(b.UserDataPath)) continue;
            b.CacheBytes = MeasureCache(b);
            found.Add(b);
        }
        return found;
    }, ct);

    private long MeasureCache(BrowserInfo b)
    {
        long total = 0;
        try
        {
            foreach (var dir in CacheDirs(b))
                total += DirectorySize(dir);
        }
        catch (Exception ex) { _log.Warn("BrowserCleanup", $"Measure failed for {b.Name}: {ex.Message}"); }
        return total;
    }

    private static IEnumerable<string> ChromiumProfiles(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(userData))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
                yield return dir;
        }
    }

    private static IEnumerable<string> CacheDirs(BrowserInfo b)
    {
        if (b.Kind == "Chromium")
        {
            foreach (var profile in ChromiumProfiles(b.UserDataPath))
            {
                yield return Path.Combine(profile, "Cache");
                yield return Path.Combine(profile, "Code Cache");
                yield return Path.Combine(profile, "GPUCache");
            }
        }
        else // Firefox: cache2 lives under LocalAppData profiles
        {
            if (!Directory.Exists(b.LocalDataPath)) yield break;
            foreach (var profile in Directory.EnumerateDirectories(b.LocalDataPath))
                yield return Path.Combine(profile, "cache2");
        }
    }

    public Task<long> ClearAsync(BrowserInfo b, bool cache, bool cookies, bool history, CancellationToken ct) => Task.Run(() =>
    {
        long freed = 0;
        try
        {
            if (cache)
                foreach (var dir in CacheDirs(b)) freed += ClearDirectory(dir);

            if (b.Kind == "Chromium")
            {
                foreach (var profile in ChromiumProfiles(b.UserDataPath))
                {
                    if (cookies) freed += DeleteFile(Path.Combine(profile, "Network", "Cookies"));
                    if (history) freed += DeleteFile(Path.Combine(profile, "History"));
                }
            }
            else // Firefox: only touch cookies (places.sqlite also holds bookmarks, so history is left alone)
            {
                if (cookies && Directory.Exists(b.UserDataPath))
                    foreach (var profile in Directory.EnumerateDirectories(b.UserDataPath))
                        freed += DeleteFile(Path.Combine(profile, "cookies.sqlite"));
            }
        }
        catch (Exception ex) { _log.Warn("BrowserCleanup", $"Clear failed for {b.Name}: {ex.Message}"); }

        _log.Info("BrowserCleanup", $"Cleared {ByteFormatter.Format(freed)} from {b.Name}.");
        return freed;
    }, ct);

    private static long DirectorySize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long total = 0;
        foreach (var fi in SafeFileEnumerator.EnumerateFiles(dir))
        {
            try { total += fi.Length; } catch (Exception) { }
        }
        return total;
    }

    // Deletes files inside a directory (leaving the directory itself), returning bytes removed.
    private static long ClearDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long freed = 0;
        foreach (var fi in SafeFileEnumerator.EnumerateFiles(dir))
        {
            try { long len = fi.Length; fi.Delete(); freed += len; }
            catch (Exception) { } // locked (browser open) or in use — skip
        }
        return freed;
    }

    private static long DeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            long len = new FileInfo(path).Length;
            File.Delete(path);
            return len;
        }
        catch (Exception) { return 0; }
    }
}
