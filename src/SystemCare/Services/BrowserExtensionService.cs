using System.Text.Json;

namespace SystemCare.Services;

public sealed class BrowserExtensionInfo
{
    public required string Browser { get; init; }
    public required string Profile { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public IReadOnlyList<string> Permissions { get; init; } = [];
    /// <summary>2 = high, 1 = medium, 0 = low.</summary>
    public int RiskLevel { get; init; }
    public string RiskReason { get; init; } = "";
}

public interface IBrowserExtensionService
{
    /// <summary>Enumerates installed extensions across Chrome/Edge/Brave profiles and Firefox,
    /// with a permission-based risk classification. Read-only; never throws.</summary>
    Task<IReadOnlyList<BrowserExtensionInfo>> ScanAsync(CancellationToken ct);
}

/// <summary>
/// Browser Extension Audit (2.17): extensions are the most common privacy hole on a PC — many
/// request "read data on all websites" and quietly outlive their usefulness. This service reads
/// the on-disk manifests (Chromium: Extensions\*\*\manifest.json; Firefox: extensions.json) and
/// classifies risk from the permission surface. Strictly read-only — removal stays in the browser,
/// where the store page and per-site controls live.
/// </summary>
public sealed class BrowserExtensionService(ILogService log) : IBrowserExtensionService
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    // Permissions that grant broad reach; combinations of these push an extension to High.
    private static readonly string[] BroadHosts = ["<all_urls>", "*://*/*", "http://*/*", "https://*/*"];
    private static readonly string[] SensitiveApis =
        ["webRequest", "webRequestBlocking", "cookies", "history", "clipboardRead", "debugger", "nativeMessaging", "management", "proxy"];

    public Task<IReadOnlyList<BrowserExtensionInfo>> ScanAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<BrowserExtensionInfo>>(() =>
        {
            var results = new List<BrowserExtensionInfo>();
            foreach (var (browser, userData) in new (string, string)[]
            {
                ("Google Chrome", Path.Combine(Local, "Google", "Chrome", "User Data")),
                ("Microsoft Edge", Path.Combine(Local, "Microsoft", "Edge", "User Data")),
                ("Brave", Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data")),
            })
                ScanChromium(browser, userData, results, ct);

            ScanFirefox(results, ct);
            return results
                .OrderByDescending(e => e.RiskLevel)
                .ThenBy(e => e.Browser).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);

    private void ScanChromium(string browser, string userData, List<BrowserExtensionInfo> results, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(userData)) return;
            foreach (var profileDir in Directory.EnumerateDirectories(userData)
                         .Where(d =>
                         {
                             string n = Path.GetFileName(d);
                             return n == "Default" || n.StartsWith("Profile ", StringComparison.Ordinal);
                         }))
            {
                ct.ThrowIfCancellationRequested();
                string extRoot = Path.Combine(profileDir, "Extensions");
                if (!Directory.Exists(extRoot)) continue;

                foreach (var extDir in Directory.EnumerateDirectories(extRoot))
                {
                    // newest version folder wins
                    var versionDir = Directory.EnumerateDirectories(extDir).OrderByDescending(Path.GetFileName).FirstOrDefault();
                    if (versionDir is null) continue;
                    string manifestPath = Path.Combine(versionDir, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                        var root = doc.RootElement;

                        string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.StartsWith("__MSG_", StringComparison.Ordinal))
                            name = ResolveChromiumLocalizedName(versionDir, root, name) ?? name;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        // Component/theme noise: skip entries without permissions AND without content scripts.
                        var permissions = ReadStringArray(root, "permissions")
                            .Concat(ReadStringArray(root, "host_permissions"))
                            .Concat(ReadContentScriptMatches(root))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var (risk, reason) = Classify(permissions);
                        results.Add(new BrowserExtensionInfo
                        {
                            Browser = browser,
                            Profile = Path.GetFileName(profileDir),
                            Name = name,
                            Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                            Permissions = permissions,
                            RiskLevel = risk,
                            RiskReason = reason,
                        });
                    }
                    catch (Exception)
                    {
                        // one unreadable manifest must not abort the audit
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Warn("Extensions", $"{browser} scan failed: {ex.Message}");
        }
    }

    private void ScanFirefox(List<BrowserExtensionInfo> results, CancellationToken ct)
    {
        try
        {
            string profiles = Path.Combine(Roaming, "Mozilla", "Firefox", "Profiles");
            if (!Directory.Exists(profiles)) return;
            foreach (var profileDir in Directory.EnumerateDirectories(profiles))
            {
                ct.ThrowIfCancellationRequested();
                string extJson = Path.Combine(profileDir, "extensions.json");
                if (!File.Exists(extJson)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(extJson));
                    if (!doc.RootElement.TryGetProperty("addons", out var addons)) continue;
                    foreach (var addon in addons.EnumerateArray())
                    {
                        // Only user-visible extensions (not built-in themes/dictionaries/system add-ons).
                        string type = addon.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        string location = addon.TryGetProperty("location", out var l) ? l.GetString() ?? "" : "";
                        if (type != "extension" || location != "app-profile") continue;

                        string name = addon.TryGetProperty("defaultLocale", out var loc) &&
                                      loc.TryGetProperty("name", out var ln) ? ln.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var permissions = new List<string>();
                        if (addon.TryGetProperty("userPermissions", out var up))
                        {
                            permissions.AddRange(ReadStringArray(up, "permissions"));
                            permissions.AddRange(ReadStringArray(up, "origins"));
                        }

                        var (risk, reason) = Classify(permissions);
                        results.Add(new BrowserExtensionInfo
                        {
                            Browser = "Firefox",
                            Profile = Path.GetFileName(profileDir),
                            Name = name,
                            Version = addon.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                            Permissions = permissions,
                            RiskLevel = risk,
                            RiskReason = reason,
                        });
                    }
                }
                catch (Exception)
                {
                    // one unreadable profile must not abort the audit
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Warn("Extensions", $"Firefox scan failed: {ex.Message}");
        }
    }

    private static (int Risk, string Reason) Classify(IReadOnlyList<string> permissions)
    {
        bool broad = permissions.Any(p => BroadHosts.Contains(p, StringComparer.OrdinalIgnoreCase));
        var sensitive = permissions.Where(p => SensitiveApis.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

        if (broad && sensitive.Count > 0)
            return (2, $"Can read every site AND uses {string.Join(", ", sensitive.Take(3))} — full visibility into your browsing.");
        if (broad)
            return (1, "Can read and change data on all websites.");
        if (sensitive.Count > 0)
            return (1, $"Uses sensitive APIs: {string.Join(", ", sensitive.Take(3))}.");
        return (0, "Limited permissions.");
    }

    private static IEnumerable<string> ReadStringArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                yield return s;
    }

    private static IEnumerable<string> ReadContentScriptMatches(JsonElement root)
    {
        if (!root.TryGetProperty("content_scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Array) yield break;
        foreach (var script in scripts.EnumerateArray())
            foreach (var match in ReadStringArray(script, "matches"))
                yield return match;
    }

    /// <summary>Chromium localizes names via _locales/&lt;locale&gt;/messages.json; resolve best-effort.</summary>
    private static string? ResolveChromiumLocalizedName(string versionDir, JsonElement manifest, string msgName)
    {
        try
        {
            string key = msgName.Trim('_')["MSG_".Length..];
            string defaultLocale = manifest.TryGetProperty("default_locale", out var dl) ? dl.GetString() ?? "en" : "en";
            foreach (var locale in new[] { defaultLocale, "en", "en_US" }.Distinct())
            {
                string messagesPath = Path.Combine(versionDir, "_locales", locale, "messages.json");
                if (!File.Exists(messagesPath)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(messagesPath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.Value.TryGetProperty("message", out var m)) return m.GetString();
                }
            }
        }
        catch (Exception) { }
        return null;
    }
}
