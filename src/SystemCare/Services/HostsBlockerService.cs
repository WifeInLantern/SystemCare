using System.Net.Http;
using System.Text;
using SystemCare.Helpers;
using SystemCare.Native;

namespace SystemCare.Services;

public record HostsStatus(bool IsApplied, int BlockedCount);

public interface IHostsBlockerService
{
    HostsStatus GetStatus();
    /// <summary>Backs up the original hosts file (once) and appends the SystemCare block section.</summary>
    Task<(bool Ok, string Message)> ApplyAsync();
    /// <summary>Removes the SystemCare block section, leaving the rest of the hosts file untouched.</summary>
    Task<(bool Ok, string Message)> RemoveAsync();
    /// <summary>True when a downloaded community blocklist is in use instead of the built-in curated list.</summary>
    bool UsingFetchedList { get; }
    /// <summary>Downloads the StevenBlack community blocklist, stores it locally, and re-applies the
    /// hosts block if it is currently active. (2.14)</summary>
    Task<(bool Ok, string Message)> RefreshFromSourceAsync();
    /// <summary>Discards the downloaded list and returns to the built-in curated list (re-applying if active).</summary>
    Task<(bool Ok, string Message)> UseBuiltInListAsync();
}

public class HostsBlockerService : IHostsBlockerService
{
    private const string BeginMarker = "# ==== SystemCare ad/tracker blocklist BEGIN (do not edit inside) ====";
    private const string EndMarker = "# ==== SystemCare ad/tracker blocklist END ====";

    private static string HostsPath =>
        Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
    private static string BackupPath => HostsPath + ".systemcare.bak";

    // 2.14: optional community blocklist, stored next to settings.json. When present it replaces
    // the built-in curated list; deleting it reverts to the built-in list.
    private const string CommunityListUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";
    private const int MaxFetchedDomains = 60000;
    private static string FetchedListPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare", "hosts-blocklist.txt");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly ILogService _log;

    public HostsBlockerService(ILogService log) => _log = log;

    public bool UsingFetchedList => File.Exists(FetchedListPath);

    /// <summary>The domain set the next Apply will write: the fetched community list when present, else the built-in one.</summary>
    private string[] ActiveDomains()
    {
        try
        {
            if (File.Exists(FetchedListPath))
            {
                var fetched = File.ReadAllLines(FetchedListPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
                    .ToArray();
                if (fetched.Length > 0) return fetched;
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Hosts", $"Fetched blocklist unreadable, falling back to built-in: {ex.Message}");
        }
        return BlockedDomains;
    }

    public async Task<(bool Ok, string Message)> RefreshFromSourceAsync()
    {
        try
        {
            string body = await Http.GetStringAsync(CommunityListUrl);

            // Parse "0.0.0.0 domain" lines; ignore comments, localhost plumbing, and duplicates.
            var domains = new List<string>(capacity: 50000);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in body.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("0.0.0.0 ", StringComparison.Ordinal)) continue;
                string domain = line[8..].Trim();
                int comment = domain.IndexOf('#');
                if (comment >= 0) domain = domain[..comment].Trim();
                if (domain.Length == 0 || domain == "0.0.0.0" || domain.Contains(' ')) continue;
                if (seen.Add(domain)) domains.Add(domain);
                if (domains.Count >= MaxFetchedDomains) break;
            }

            if (domains.Count < 1000)
                return (false, "The downloaded list looked wrong (too few entries) — keeping the current list.");

            Directory.CreateDirectory(Path.GetDirectoryName(FetchedListPath)!);
            await File.WriteAllLinesAsync(FetchedListPath, domains);
            _log.Info("Hosts", $"Fetched community blocklist: {domains.Count} domains.");

            if (GetStatus().IsApplied)
            {
                var removed = await RemoveAsync();
                if (!removed.Ok) return removed;
                var applied = await ApplyAsync();
                if (!applied.Ok) return applied;
            }
            return (true, $"Community blocklist updated — {domains.Count:N0} domains (StevenBlack).");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Download failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Download timed out — try again later.");
        }
        catch (Exception ex)
        {
            _log.Error("Hosts", "Blocklist refresh failed", ex);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> UseBuiltInListAsync()
    {
        try
        {
            if (File.Exists(FetchedListPath)) File.Delete(FetchedListPath);
            if (GetStatus().IsApplied)
            {
                var removed = await RemoveAsync();
                if (!removed.Ok) return removed;
                var applied = await ApplyAsync();
                if (!applied.Ok) return applied;
            }
            return (true, "Back on the built-in curated list.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // A curated, conservative set of well-known ad/telemetry hosts. Kept intentionally small so it
    // never breaks site logins or content — just the obvious trackers.
    private static readonly string[] BlockedDomains =
    [
        "ads.google.com", "adservice.google.com", "pagead2.googlesyndication.com",
        "googleadservices.com", "www.googleadservices.com", "doubleclick.net", "ad.doubleclick.net",
        "stats.g.doubleclick.net", "static.doubleclick.net",
        "ads.youtube.com", "analytics.google.com", "www.google-analytics.com", "ssl.google-analytics.com",
        "connect.facebook.net", "an.facebook.com", "ads.facebook.com", "pixel.facebook.com",
        "ads.yahoo.com", "analytics.yahoo.com",
        "ads.twitter.com", "analytics.twitter.com",
        "ads.linkedin.com", "px.ads.linkedin.com",
        "adnxs.com", "ib.adnxs.com", "secure.adnxs.com",
        "scorecardresearch.com", "sb.scorecardresearch.com",
        "criteo.com", "static.criteo.net", "bidder.criteo.com",
        "taboola.com", "cdn.taboola.com", "trc.taboola.com",
        "outbrain.com", "widgets.outbrain.com",
        "amazon-adsystem.com", "aax.amazon-adsystem.com",
        "adcolony.com", "app-measurement.com",
        "hotjar.com", "static.hotjar.com",
        "mixpanel.com", "api.mixpanel.com",
        "segment.io", "cdn.segment.com",
        "bat.bing.com", "flex.msn.com",
        "telemetry.microsoft.com", "vortex.data.microsoft.com", "watson.telemetry.microsoft.com",
    ];

    public HostsStatus GetStatus()
    {
        try
        {
            if (!File.Exists(HostsPath)) return new HostsStatus(false, 0);
            string text = File.ReadAllText(HostsPath);
            bool applied = text.Contains(BeginMarker, StringComparison.Ordinal);
            return new HostsStatus(applied, applied ? ActiveDomains().Length : 0);
        }
        catch (Exception ex)
        {
            _log.Warn("Hosts", $"Status read failed: {ex.Message}");
            return new HostsStatus(false, 0);
        }
    }

    public async Task<(bool Ok, string Message)> ApplyAsync()
    {
        try
        {
            string original = File.Exists(HostsPath) ? await File.ReadAllTextAsync(HostsPath) : "";
            if (original.Contains(BeginMarker, StringComparison.Ordinal))
                return (true, "Blocklist is already applied.");

            // One-time backup of the pristine hosts file.
            if (!File.Exists(BackupPath))
                await File.WriteAllTextAsync(BackupPath, original, Encoding.ASCII);

            var domains = ActiveDomains();
            var sb = new StringBuilder(original);
            if (original.Length > 0 && !original.EndsWith('\n')) sb.Append("\r\n");
            sb.Append("\r\n").Append(BeginMarker).Append("\r\n");
            foreach (var d in domains) sb.Append("0.0.0.0 ").Append(d).Append("\r\n");
            sb.Append(EndMarker).Append("\r\n");

            await File.WriteAllTextAsync(HostsPath, sb.ToString(), Encoding.ASCII);
            NativeMethods.DnsFlushResolverCache(); // in-process DLL call instead of spawning ipconfig.exe
            _log.Info("Hosts", $"Applied blocklist ({domains.Length} domains).");
            return (true, $"Blocking {domains.Length:N0} ad/tracker domains. A backup of your original hosts file was saved.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied writing the hosts file — administrator rights are required.");
        }
        catch (Exception ex)
        {
            _log.Error("Hosts", "Apply failed", ex);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> RemoveAsync()
    {
        try
        {
            if (!File.Exists(HostsPath)) return (true, "Nothing to remove.");
            string text = await File.ReadAllTextAsync(HostsPath);

            int begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            int end = text.IndexOf(EndMarker, StringComparison.Ordinal);
            if (begin < 0 || end < 0) return (true, "Blocklist wasn't applied.");

            end += EndMarker.Length;
            // Trim the leading blank line(s) we added before the block, too.
            int trimStart = begin;
            while (trimStart > 0 && (text[trimStart - 1] == '\n' || text[trimStart - 1] == '\r')) trimStart--;

            string cleaned = (text[..trimStart] + text[end..]).TrimEnd('\r', '\n') + "\r\n";
            await File.WriteAllTextAsync(HostsPath, cleaned, Encoding.ASCII);
            NativeMethods.DnsFlushResolverCache(); // in-process DLL call instead of spawning ipconfig.exe
            _log.Info("Hosts", "Removed blocklist.");
            return (true, "Blocklist removed — your original hosts entries are untouched.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied writing the hosts file — administrator rights are required.");
        }
        catch (Exception ex)
        {
            _log.Error("Hosts", "Remove failed", ex);
            return (false, ex.Message);
        }
    }
}
