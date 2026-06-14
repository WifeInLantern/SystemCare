using System.Text.RegularExpressions;
using Microsoft.Win32;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ILeftoverScanService
{
    /// <summary>
    /// Phase 1 — capture candidate leftover locations while the app's registry data is still present.
    /// Read-only; does not test whether the uninstaller will remove them (that's <see cref="VerifyAsync"/>).
    /// </summary>
    LeftoverPlan CaptureCandidates(InstalledApp app);

    /// <summary>
    /// Phase 2 — re-test the captured candidates and return only those that still exist now (i.e. the
    /// uninstaller left them behind). Folder sizes are measured here.
    /// </summary>
    Task<IReadOnlyList<LeftoverItem>> VerifyAsync(LeftoverPlan plan, CancellationToken ct);

    /// <summary>
    /// Removes the given leftovers: files/folders/shortcuts to the Recycle Bin (recoverable); registry
    /// keys/values via backup-then-delete (recoverable from a timestamped .reg backup).
    /// </summary>
    Task<LeftoverRemoveResult> RemoveAsync(IEnumerable<LeftoverItem> items, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>
/// Conservative per-app leftover finder. It only proposes folders/shortcuts/registry keys that clearly
/// belong to the uninstalled program (token-matched against its name and publisher), confines registry
/// hits to the app's own Uninstall and publisher namespace, cross-checks against other installed apps,
/// and surfaces only what survives the uninstaller. Every removal is recoverable.
/// </summary>
public class LeftoverScanService(
    IFileOperationService fileOps,
    IRegistryCleanerService registry,
    IInstalledAppsService apps) : ILeftoverScanService
{
    // Company suffixes / filler dropped before tokenizing so "Mozilla Corporation" matches "Mozilla".
    private static readonly HashSet<string> CompanyNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "inc", "llc", "ltd", "corp", "corporation", "co", "gmbh", "ab", "sa", "company",
        "limited", "incorporated", "the", "and",
    };

    // Generic words that must never, on their own, mark a folder/key as a leftover — otherwise
    // uninstalling e.g. "Media Player" or "Driver Booster" could flag an unrelated %APPDATA%\Media
    // or \Player folder. A distinctive (brand/product) token is always also required.
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "player", "media", "video", "audio", "music", "photo", "update", "updater", "setup",
        "install", "installer", "uninstall", "tools", "tool", "data", "files", "file", "core",
        "app", "apps", "client", "manager", "service", "services", "driver", "drivers", "console",
        "launcher", "browser", "edition", "version", "studio", "desktop", "windows", "program",
        "programs", "software", "helper", "host", "agent", "runtime", "system", "settings",
    };

    private static readonly (RegistryHive Hive, string Path)[] UninstallRoots =
    [
        (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    private static readonly (RegistryHive Hive, string Path)[] SoftwareRoots =
    [
        (RegistryHive.CurrentUser, @"Software"),
        (RegistryHive.LocalMachine, @"Software"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node"),
    ];

    // ---------- phase 1: capture ----------

    public LeftoverPlan CaptureCandidates(InstalledApp app)
    {
        var plan = new LeftoverPlan { App = app };

        var nameTokens = Tokenize(app.Name);
        var publisherTokens = Tokenize(app.Publisher);
        // Distinctive = the brand/product tokens specific enough to identify this app on their own
        // (4+ chars, not a generic word). Matching on the whole set — not just the longest token —
        // catches either word of a name like "Mozilla Firefox" while never matching on "media"/"player".
        var distinctive = nameTokens
            .Where(t => t.Length >= 4 && !GenericTokens.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool Matches(string leafName)
        {
            var tokens = Tokenize(leafName);
            if (tokens.Overlaps(distinctive)) return true;
            if (publisherTokens.Count > 0 && publisherTokens.IsSubsetOf(tokens) && tokens.Overlaps(nameTokens))
                return true;
            return false;
        }

        var protectedRoots = ProtectedRoots();
        var otherLocations = OtherInstallLocations(app);

        bool AcceptFolder(string path)
        {
            string norm = Normalize(path);
            if (norm.Length <= 3) return false;                              // drive root / too shallow
            if (protectedRoots.Contains(norm)) return false;                 // a scan root itself
            foreach (var other in otherLocations)                            // shared with another app
            {
                if (norm.Equals(other, StringComparison.OrdinalIgnoreCase)) return false;
                if (norm.StartsWith(other + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                if (other.StartsWith(norm + "\\", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddFolder(string path, string reason)
        {
            if (!AcceptFolder(path)) return;
            if (!seen.Add("F|" + Normalize(path))) return;
            plan.Candidates.Add(new LeftoverItem { Kind = LeftoverKind.Folder, Path = path, Reason = reason });
        }
        void AddShortcut(string path, string reason)
        {
            if (!seen.Add("S|" + Normalize(path))) return;
            plan.Candidates.Add(new LeftoverItem { Kind = LeftoverKind.Shortcut, Path = path, Reason = reason });
        }

        // The install folder and its vendor parent.
        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            AddFolder(app.InstallLocation!, "Install folder");
            try
            {
                var parent = Directory.GetParent(app.InstallLocation!.TrimEnd('\\'));
                if (parent is not null && Matches(parent.Name)) AddFolder(parent.FullName, "Program folder");
            }
            catch (Exception) { }
        }

        // Per-app data folders (top-level subfolders only).
        foreach (var (root, label) in DataRoots())
            foreach (var dir in TopLevelDirs(root))
                if (Matches(LeafName(dir))) AddFolder(dir, label);

        // Start Menu: matching folders and loose shortcuts.
        foreach (var menu in StartMenuRoots())
        {
            foreach (var dir in TopLevelDirs(menu))
                if (Matches(LeafName(dir))) AddFolder(dir, "Start Menu folder");
            foreach (var lnk in TopLevelShortcuts(menu))
                if (Matches(ShortcutName(lnk))) AddShortcut(lnk, "Start Menu shortcut");
        }

        // Desktop shortcuts.
        foreach (var desk in DesktopRoots())
            foreach (var lnk in TopLevelShortcuts(desk))
                if (Matches(ShortcutName(lnk))) AddShortcut(lnk, "Desktop shortcut");

        CaptureRegistry(app, plan, Matches, publisherTokens, distinctive);
        return plan;
    }

    private void CaptureRegistry(
        InstalledApp app, LeftoverPlan plan, Func<string, bool> matches,
        HashSet<string> publisherTokens, HashSet<string> distinctive)
    {
        // The app's own Uninstall entry (located by DisplayName), in case the uninstaller orphans it.
        foreach (var (hive, path) in UninstallRoots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(path);
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = key.OpenSubKey(sub);
                        if (entry?.GetValue("DisplayName") as string == app.Name)
                            plan.Candidates.Add(new LeftoverItem
                            {
                                Kind = LeftoverKind.RegistryKey,
                                Hive = hive,
                                SubKeyPath = $@"{path}\{sub}",
                                Reason = "Uninstall registry key",
                            });
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        // Publisher\Product (and direct AppName) keys under Software, confined to the app's namespace.
        foreach (var (hive, root) in SoftwareRoots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var softwareKey = baseKey.OpenSubKey(root);
                if (softwareKey is null) continue;

                foreach (var vendor in softwareKey.GetSubKeyNames())
                {
                    var vendorTokens = Tokenize(vendor);

                    // Publisher folder: only descend into its product subkeys (never propose the whole vendor key).
                    if (publisherTokens.Count > 0 && publisherTokens.IsSubsetOf(vendorTokens))
                    {
                        try
                        {
                            using var vendorKey = softwareKey.OpenSubKey($@"{vendor}");
                            foreach (var product in vendorKey?.GetSubKeyNames() ?? [])
                                if (matches(product) || Tokenize(product).Overlaps(distinctive))
                                    plan.Candidates.Add(new LeftoverItem
                                    {
                                        Kind = LeftoverKind.RegistryKey,
                                        Hive = hive,
                                        SubKeyPath = $@"{root}\{vendor}\{product}",
                                        Reason = "Publisher registry key",
                                    });
                        }
                        catch (Exception) { }
                        continue;
                    }

                    // App stored directly under Software\<AppName>.
                    if (vendorTokens.Overlaps(distinctive))
                        plan.Candidates.Add(new LeftoverItem
                        {
                            Kind = LeftoverKind.RegistryKey,
                            Hive = hive,
                            SubKeyPath = $@"{root}\{vendor}",
                            Reason = "App registry key",
                        });
                }
            }
            catch (Exception) { }
        }
    }

    // ---------- phase 2: verify ----------

    public Task<IReadOnlyList<LeftoverItem>> VerifyAsync(LeftoverPlan plan, CancellationToken ct) => Task.Run(() =>
    {
        var survivors = new List<LeftoverItem>();
        foreach (var item in plan.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (item.IsRegistry)
            {
                if (RegistryExists(item)) survivors.Add(item);
            }
            else if (item.Kind == LeftoverKind.Folder)
            {
                if (Directory.Exists(item.Path))
                {
                    item.SizeBytes = SafeFileEnumerator.Measure(item.Path!).Bytes;
                    survivors.Add(item);
                }
            }
            else // shortcut
            {
                if (File.Exists(item.Path)) survivors.Add(item);
            }
        }
        return (IReadOnlyList<LeftoverItem>)survivors;
    }, ct);

    private static bool RegistryExists(LeftoverItem item)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(item.Hive, item.View);
            using var key = baseKey.OpenSubKey(item.SubKeyPath!);
            if (key is null) return false;
            return item.ValueName is null || key.GetValue(item.ValueName) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---------- removal ----------

    public async Task<LeftoverRemoveResult> RemoveAsync(IEnumerable<LeftoverItem> items, IProgress<string>? progress, CancellationToken ct)
    {
        var list = items.ToList();
        var result = new LeftoverRemoveResult();

        foreach (var item in list.Where(i => !i.IsRegistry))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Removing {item.DisplayPath}…");
            bool ok = await Task.Run(() => fileOps.SendToRecycleBin(item.Path!), ct);
            if (ok)
            {
                result.FilesRemoved++;
                result.BytesRemoved += item.SizeBytes;
            }
            else
            {
                result.Skipped++;
            }
        }

        var registryItems = list.Where(i => i.IsRegistry).Select(i => i.ToRegistryIssue()).ToList();
        if (registryItems.Count > 0)
        {
            var cleaned = await registry.CleanAsync(registryItems, progress, ct);
            result.RegistryRemoved = cleaned.Removed;
            result.Skipped += cleaned.Skipped;
            result.RegistryBackupFolder = cleaned.BackupFolder;
        }

        return result;
    }

    // ---------- helpers ----------

    private static HashSet<string> Tokenize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new(StringComparer.OrdinalIgnoreCase);
        return Regex.Split(s.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(t => t.Length >= 2 && !CompanyNoise.Contains(t) && !IsVersionToken(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsVersionToken(string token) => Regex.IsMatch(token, @"^\d+([._]\d+)*$");

    private static string Normalize(string path) => path.Trim().Trim('"').TrimEnd('\\');

    private static string LeafName(string path) => new DirectoryInfo(Normalize(path)).Name;

    private static string ShortcutName(string lnkPath) => Path.GetFileNameWithoutExtension(lnkPath);

    private IReadOnlyList<string> OtherInstallLocations(InstalledApp app)
    {
        var set = new List<string>();
        try
        {
            foreach (var other in apps.GetInstalledAppsAsync().GetAwaiter().GetResult())
            {
                if (other.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(other.InstallLocation))
                    set.Add(Normalize(other.InstallLocation!));
            }
        }
        catch (Exception) { }
        return set;
    }

    private static IEnumerable<string> TopLevelDirs(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateDirectories(root, "*", SafeFileEnumerator.TopLevelOptions())
                : [];
        }
        catch (Exception) { return []; }
    }

    private static IEnumerable<string> TopLevelShortcuts(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*.lnk", SafeFileEnumerator.TopLevelOptions())
                : [];
        }
        catch (Exception) { return []; }
    }

    private static IEnumerable<(string Root, string Label)> DataRoots()
    {
        yield return (Folder(Environment.SpecialFolder.ApplicationData), "AppData folder");
        yield return (Folder(Environment.SpecialFolder.LocalApplicationData), "Local AppData folder");
        yield return (Path.Combine(Folder(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"), "LocalLow folder");
        yield return (Folder(Environment.SpecialFolder.CommonApplicationData), "ProgramData folder");
        yield return (Folder(Environment.SpecialFolder.ProgramFiles), "Program Files folder");
        yield return (Folder(Environment.SpecialFolder.ProgramFilesX86), "Program Files folder");
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        yield return Path.Combine(Folder(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
        yield return Path.Combine(Folder(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
    }

    private static IEnumerable<string> DesktopRoots()
    {
        yield return Folder(Environment.SpecialFolder.DesktopDirectory);
        yield return Folder(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    private static HashSet<string> ProtectedRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, _) in DataRoots()) roots.Add(Normalize(root));
        foreach (var menu in StartMenuRoots()) roots.Add(Normalize(menu));
        foreach (var desk in DesktopRoots()) roots.Add(Normalize(desk));
        roots.Add(Normalize(Folder(Environment.SpecialFolder.Windows)));
        roots.Add(Normalize(Folder(Environment.SpecialFolder.UserProfile)));
        return roots;
    }

    private static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
}
