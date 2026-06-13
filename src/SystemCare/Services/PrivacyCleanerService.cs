using System.Diagnostics;
using Microsoft.Win32;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Native;

namespace SystemCare.Services;

public interface IPrivacyCleanerService
{
    IReadOnlyList<PrivacyCategory> Categories { get; }
    /// <summary>Process names (chrome/msedge/firefox) of browsers currently running.</summary>
    HashSet<string> GetRunningBrowsers();
    bool TryCloseBrowser(string processName);
    Task<List<PrivacyCategoryStatus>> ScanAsync(CancellationToken ct);
    Task<PrivacyCleanResult> CleanAsync(IEnumerable<string> categoryIds, CancellationToken ct);
}

public class PrivacyCleanerService : IPrivacyCleanerService
{
    private static string Local(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);

    private static string Roaming(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), .. parts]);

    // ---------- browser profile discovery ----------

    private static IEnumerable<string> ChromiumProfiles(string userDataRoot)
    {
        if (!Directory.Exists(userDataRoot)) yield break;
        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(userDataRoot, "*", SafeFileEnumerator.TopLevelOptions())
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
        foreach (var dir in dirs) yield return dir;
    }

    private static IEnumerable<string> ChromiumHistoryFiles(string userDataRoot)
    {
        foreach (var profile in ChromiumProfiles(userDataRoot))
        {
            yield return Path.Combine(profile, "History");
            yield return Path.Combine(profile, "History-journal");
            yield return Path.Combine(profile, "Visited Links");
            yield return Path.Combine(profile, "Top Sites");
            yield return Path.Combine(profile, "Top Sites-journal");
        }
    }

    private static IEnumerable<string> ChromiumCookieFiles(string userDataRoot)
    {
        foreach (var profile in ChromiumProfiles(userDataRoot))
        {
            yield return Path.Combine(profile, "Network", "Cookies");
            yield return Path.Combine(profile, "Network", "Cookies-journal");
        }
    }

    private static IEnumerable<string> ChromiumCacheDirs(string userDataRoot)
    {
        foreach (var profile in ChromiumProfiles(userDataRoot))
        {
            yield return Path.Combine(profile, "Cache", "Cache_Data");
            yield return Path.Combine(profile, "Code Cache");
            yield return Path.Combine(profile, "GPUCache");
        }
    }

    private static IEnumerable<string> FirefoxProfileDirs() =>
        Directory.Exists(Roaming("Mozilla", "Firefox", "Profiles"))
            ? Directory.EnumerateDirectories(Roaming("Mozilla", "Firefox", "Profiles"))
            : [];

    private static string ChromeRoot => Local("Google", "Chrome", "User Data");
    private static string EdgeRoot => Local("Microsoft", "Edge", "User Data");

    public IReadOnlyList<PrivacyCategory> Categories { get; } =
    [
        // ----- Chrome -----
        new PrivacyCategory
        {
            Id = "chrome-history", Group = "Google Chrome", Name = "Browsing history",
            Description = "Visited pages and top-sites data (bookmarks are kept)",
            BrowserProcess = "chrome",
            GetPaths = () => ChromiumHistoryFiles(ChromeRoot),
        },
        new PrivacyCategory
        {
            Id = "chrome-cookies", Group = "Google Chrome", Name = "Cookies",
            Description = "Signs you out of websites", EnabledByDefault = false,
            BrowserProcess = "chrome",
            GetPaths = () => ChromiumCookieFiles(ChromeRoot),
        },
        new PrivacyCategory
        {
            Id = "chrome-cache", Group = "Google Chrome", Name = "Cache",
            Description = "Stored page resources", Kind = PrivacyKind.DirectoryContents,
            BrowserProcess = "chrome",
            GetPaths = () => ChromiumCacheDirs(ChromeRoot),
        },
        // ----- Edge -----
        new PrivacyCategory
        {
            Id = "edge-history", Group = "Microsoft Edge", Name = "Browsing history",
            Description = "Visited pages and top-sites data (favorites are kept)",
            BrowserProcess = "msedge",
            GetPaths = () => ChromiumHistoryFiles(EdgeRoot),
        },
        new PrivacyCategory
        {
            Id = "edge-cookies", Group = "Microsoft Edge", Name = "Cookies",
            Description = "Signs you out of websites", EnabledByDefault = false,
            BrowserProcess = "msedge",
            GetPaths = () => ChromiumCookieFiles(EdgeRoot),
        },
        new PrivacyCategory
        {
            Id = "edge-cache", Group = "Microsoft Edge", Name = "Cache",
            Description = "Stored page resources", Kind = PrivacyKind.DirectoryContents,
            BrowserProcess = "msedge",
            GetPaths = () => ChromiumCacheDirs(EdgeRoot),
        },
        // ----- Firefox (places.sqlite holds bookmarks AND history — never touched) -----
        new PrivacyCategory
        {
            Id = "firefox-cookies", Group = "Mozilla Firefox", Name = "Cookies & form history",
            Description = "Signs you out of websites (history is not supported — it shares a file with bookmarks)",
            EnabledByDefault = false,
            BrowserProcess = "firefox",
            GetPaths = () => FirefoxProfileDirs().SelectMany(p => new[]
            {
                Path.Combine(p, "cookies.sqlite"),
                Path.Combine(p, "cookies.sqlite-wal"),
                Path.Combine(p, "formhistory.sqlite"),
            }),
        },
        new PrivacyCategory
        {
            Id = "firefox-cache", Group = "Mozilla Firefox", Name = "Cache",
            Description = "Stored page resources", Kind = PrivacyKind.DirectoryContents,
            BrowserProcess = "firefox",
            GetPaths = () => FirefoxProfileDirs()
                .Select(p => Path.Combine(Local("Mozilla", "Firefox", "Profiles"), Path.GetFileName(p), "cache2"))
                .Where(Directory.Exists),
        },
        // ----- Windows traces -----
        new PrivacyCategory
        {
            Id = "win-recent", Group = "Windows", Name = "Recent files & jump lists",
            Description = "Recently-opened file shortcuts and taskbar jump lists",
            Kind = PrivacyKind.DirectoryContents,
            GetPaths = () => [Environment.GetFolderPath(Environment.SpecialFolder.Recent)],
        },
        new PrivacyCategory
        {
            Id = "win-runmru", Group = "Windows", Name = "Run dialog history",
            Description = "Commands typed into Win+R",
            Kind = PrivacyKind.RegistryValues,
            RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
        },
        new PrivacyCategory
        {
            Id = "win-recentdocs", Group = "Windows", Name = "Recent documents list",
            Description = "Explorer's per-extension recent document history",
            Kind = PrivacyKind.RegistryValues,
            RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs",
        },
        new PrivacyCategory
        {
            Id = "sys-dns", Group = "System", Name = "DNS cache",
            Description = "Cached DNS lookups (reveals visited hosts)",
            Kind = PrivacyKind.DnsCache,
        },
        new PrivacyCategory
        {
            Id = "sys-clipboard", Group = "System", Name = "Clipboard",
            Description = "Current clipboard contents",
            Kind = PrivacyKind.Clipboard,
        },
    ];

    public HashSet<string> GetRunningBrowsers()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "chrome", "msedge", "firefox" })
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0) running.Add(name);
            }
            catch (Exception) { }
        }
        return running;
    }

    public bool TryCloseBrowser(string processName)
    {
        try
        {
            bool any = false;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    // Polite close only — never Kill: the user may have unsaved work.
                    if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                        any = true;
                }
            }
            return any;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task<List<PrivacyCategoryStatus>> ScanAsync(CancellationToken ct) => Task.Run(() =>
    {
        var running = GetRunningBrowsers();
        var statuses = new List<PrivacyCategoryStatus>();

        foreach (var category in Categories)
        {
            ct.ThrowIfCancellationRequested();
            var status = new PrivacyCategoryStatus
            {
                Category = category,
                BlockedByRunningBrowser = category.BrowserProcess is not null && running.Contains(category.BrowserProcess),
            };

            try
            {
                switch (category.Kind)
                {
                    case PrivacyKind.Files:
                        foreach (var path in category.GetPaths().Where(File.Exists))
                        {
                            try
                            {
                                status.Bytes += new FileInfo(path).Length;
                                status.ItemCount++;
                            }
                            catch (Exception) { }
                        }
                        break;
                    case PrivacyKind.DirectoryContents:
                        foreach (var dir in category.GetPaths())
                        {
                            var (bytes, files) = SafeFileEnumerator.Measure(dir);
                            status.Bytes += bytes;
                            status.ItemCount += files;
                        }
                        break;
                    case PrivacyKind.RegistryValues:
                        using (var key = Registry.CurrentUser.OpenSubKey(category.RegistryKeyPath!))
                        {
                            if (key is not null)
                                status.ItemCount = key.GetValueNames().Count(n => !string.IsNullOrEmpty(n)) + key.GetSubKeyNames().Length;
                        }
                        break;
                    case PrivacyKind.DnsCache:
                    case PrivacyKind.Clipboard:
                        status.ItemCount = 1; // always available to clear
                        break;
                }
            }
            catch (Exception) { }

            statuses.Add(status);
        }
        return statuses;
    }, ct);

    public Task<PrivacyCleanResult> CleanAsync(IEnumerable<string> categoryIds, CancellationToken ct) => Task.Run(() =>
    {
        var wanted = categoryIds.ToHashSet();
        var result = new PrivacyCleanResult();

        foreach (var category in Categories.Where(c => wanted.Contains(c.Id)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (category.Kind)
                {
                    case PrivacyKind.Files:
                        foreach (var path in category.GetPaths().Where(File.Exists))
                            DeleteFileCounted(path, result);
                        break;

                    case PrivacyKind.DirectoryContents:
                        foreach (var dir in category.GetPaths())
                            foreach (var file in SafeFileEnumerator.EnumerateFiles(dir))
                                DeleteFileCounted(file.FullName, result);
                        break;

                    case PrivacyKind.RegistryValues:
                        using (var key = Registry.CurrentUser.OpenSubKey(category.RegistryKeyPath!, writable: true))
                        {
                            if (key is not null)
                            {
                                foreach (var name in key.GetValueNames())
                                {
                                    try
                                    {
                                        key.DeleteValue(name, throwOnMissingValue: false);
                                        result.ItemsRemoved++;
                                    }
                                    catch (Exception) { result.ItemsSkipped++; }
                                }
                                foreach (var sub in key.GetSubKeyNames())
                                {
                                    try
                                    {
                                        key.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
                                        result.ItemsRemoved++;
                                    }
                                    catch (Exception) { result.ItemsSkipped++; }
                                }
                            }
                        }
                        break;

                    case PrivacyKind.DnsCache:
                        if (NativeMethods.DnsFlushResolverCache() != 0)
                        {
                            result.ItemsRemoved++;
                        }
                        else
                        {
                            // fallback: hidden ipconfig /flushdns
                            try
                            {
                                using var ipconfig = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
                                {
                                    CreateNoWindow = true,
                                    UseShellExecute = false,
                                });
                                ipconfig?.WaitForExit(5000);
                                result.ItemsRemoved++;
                            }
                            catch (Exception) { result.ItemsSkipped++; }
                        }
                        break;

                    case PrivacyKind.Clipboard:
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                System.Windows.Clipboard.Clear();
                                result.ItemsRemoved++;
                            }
                            catch (Exception) { result.ItemsSkipped++; }
                        });
                        break;
                }
            }
            catch (Exception) { }
        }
        return result;
    }, ct);

    private static void DeleteFileCounted(string path, PrivacyCleanResult result)
    {
        try
        {
            long size = 0;
            try { size = new FileInfo(path).Length; } catch (Exception) { }
            File.Delete(path);
            result.BytesRemoved += size;
            result.ItemsRemoved++;
        }
        catch (IOException) { result.ItemsSkipped++; }
        catch (UnauthorizedAccessException) { result.ItemsSkipped++; }
        catch (Exception) { result.ItemsSkipped++; }
    }
}
