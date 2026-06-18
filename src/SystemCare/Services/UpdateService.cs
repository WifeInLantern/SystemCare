using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    UpdateInfo? LatestAvailable { get; }
    /// <summary>Returns a newer release if one is available, else null. Never throws.</summary>
    Task<UpdateInfo?> CheckAsync();
    /// <summary>Downloads the release installer to the Downloads folder; returns the path or null.</summary>
    Task<string?> DownloadAsync(IProgress<double>? progress, CancellationToken ct);
    /// <summary>Launches a downloaded file (the installer). Returns true if the process actually started.</summary>
    bool Launch(string path);
}

public class UpdateService(ISettingsService settings, ILogService log) : IUpdateService
{
    // Default to this build's own GitHub repo's latest release.
    private const string DefaultFeedUrl = "https://api.github.com/repos/WifeInLantern/SystemCare/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public UpdateInfo? LatestAvailable { get; private set; }

    private string FeedUrl =>
        string.IsNullOrWhiteSpace(settings.Current.UpdateFeedUrl) ? DefaultFeedUrl : settings.Current.UpdateFeedUrl;

    public async Task<UpdateInfo?> CheckAsync()
    {
        settings.Current.LastUpdateCheckUtc = DateTime.UtcNow;
        settings.Save();
        LatestAvailable = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
            ApplyGitHubHeaders(request);
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null; // 404 = no release / private without token

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            string version = (GetStr(root, "version") ?? GetStr(root, "tag_name") ?? "").TrimStart('v', 'V');
            string notes = GetStr(root, "notes") ?? GetStr(root, "body") ?? "";
            string releaseUrl = GetStr(root, "html_url") ?? GetStr(root, "url") ?? "";

            // Pick the best downloadable asset (prefer the installer, then any .exe).
            string? assetApi = null, assetDownload = null, assetName = "", checksumUrl = null;
            long assetSize = 0;
            bool hasAssetsArray = root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array;
            if (hasAssetsArray)
            {
                JsonElement? best = null;
                foreach (var a in assets.EnumerateArray())
                {
                    string n = GetStr(a, "name") ?? "";
                    // Only ever treat an .exe as the installer — never fall back to launching an
                    // arbitrary asset (a .zip, source archive, checksum file, …) as if it were one.
                    if (n.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase)) { best = a; break; }
                    if (best is null && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) best = a;
                }
                if (best is JsonElement chosen)
                {
                    assetApi = GetStr(chosen, "url");
                    assetDownload = GetStr(chosen, "browser_download_url");
                    assetName = GetStr(chosen, "name") ?? "SystemCare-Setup.exe";
                    assetSize = chosen.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;

                    // Find the matching SHA-256 checksum asset (prefer "<installer>.sha256", else any *.sha256).
                    string want = assetName + ".sha256";
                    foreach (var a in assets.EnumerateArray())
                    {
                        string n = GetStr(a, "name") ?? "";
                        if (n.Equals(want, StringComparison.OrdinalIgnoreCase)) { checksumUrl = GetStr(a, "browser_download_url"); break; }
                        if (checksumUrl is null && n.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) checksumUrl = GetStr(a, "browser_download_url");
                    }
                }
            }
            // A simple custom feed (no GitHub-style "assets" array) may put the download url under "url".
            // Never do this for a GitHub release, whose root "url" is the release API endpoint, not a file.
            if (assetDownload is null && assetApi is null && !hasAssetsArray)
                assetDownload = GetStr(root, "url");

            if (Version.TryParse(version, out var remote) &&
                Version.TryParse(CurrentVersion, out var current) &&
                remote > current)
            {
                LatestAvailable = new UpdateInfo
                {
                    Version = version, Notes = notes, ReleaseUrl = releaseUrl,
                    AssetApiUrl = assetApi, AssetDownloadUrl = assetDownload,
                    AssetName = string.IsNullOrEmpty(assetName) ? "SystemCare-Setup.exe" : assetName,
                    AssetSize = assetSize,
                    ChecksumUrl = checksumUrl,
                };
                return LatestAvailable;
            }
        }
        catch (Exception)
        {
            // offline / bad feed / parse error — treat as "no update"
        }
        return null;
    }

    public async Task<string?> DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var info = LatestAvailable;
        if (info is null || !info.HasAsset) return null;

        bool hasToken = !string.IsNullOrWhiteSpace(settings.Current.UpdateGitHubToken);
        // For private repos, download via the asset API url with octet-stream + auth (302 → signed URL).
        string url = hasToken && !string.IsNullOrEmpty(info.AssetApiUrl) ? info.AssetApiUrl! : info.AssetDownloadUrl!;

        // The downloaded file is launched with administrator rights, so only ever fetch it over HTTPS —
        // a plaintext download could be tampered with in transit (MITM) to run arbitrary code as admin.
        if (!IsHttps(url)) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("SystemCare-UpdateCheck");
            // Only send the token to GitHub itself, never to an arbitrary asset/redirect host.
            if (hasToken && IsGitHubHost(url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Current.UpdateGitHubToken);
                request.Headers.Accept.ParseAdd("application/octet-stream");
            }

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            string path = Path.Combine(downloads, info.AssetName);
            // Stream to a temporary .part file, verify it, then atomically rename — so a truncated or
            // tampered download is never left under the real name nor launched as the installer.
            string partPath = path + ".part";

            long? total = response.Content.Headers.ContentLength ?? (info.AssetSize > 0 ? info.AssetSize : null);
            byte[] hash;
            long read = 0;
            try
            {
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    hasher.AppendData(buffer, 0, n);
                    read += n;
                    if (total is > 0) progress?.Report(read * 100.0 / total.Value);
                }
                hash = hasher.GetHashAndReset();
            }
            catch (Exception)
            {
                TryDelete(partPath);
                throw;
            }

            // 1) Length: a known expected size must match exactly (catches a connection closed mid-stream).
            if (total is > 0 && read != total.Value)
            {
                log.Warn("Updater", $"Download incomplete: got {read} of {total.Value} bytes — discarding.");
                TryDelete(partPath);
                return null;
            }

            // 2) Checksum: when the release publishes a .sha256, the bytes must match it.
            if (!string.IsNullOrEmpty(info.ChecksumUrl))
            {
                string? expected = await VerifyChecksumAsync(info.ChecksumUrl!, ct);
                string actual = Convert.ToHexString(hash);
                if (expected is null)
                {
                    log.Warn("Updater", "Could not fetch the published checksum — discarding the download.");
                    TryDelete(partPath);
                    return null;
                }
                if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                {
                    log.Error("Updater", $"Checksum mismatch (expected {expected}, got {actual}) — discarding the download.");
                    TryDelete(partPath);
                    return null;
                }
                log.Info("Updater", "Installer SHA-256 verified.");
            }
            else
            {
                log.Info("Updater", total is > 0
                    ? "Installer size verified (no published checksum)."
                    : "Installer downloaded (no size or checksum to verify against).");
            }

            File.Move(partPath, path, overwrite: true);
            progress?.Report(100);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Fetches and parses the first SHA-256 hex token from a published .sha256 file, or null.</summary>
    private async Task<string?> VerifyChecksumAsync(string url, CancellationToken ct)
    {
        if (!IsHttps(url)) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("SystemCare-UpdateCheck");
            if (!string.IsNullOrWhiteSpace(settings.Current.UpdateGitHubToken) && IsGitHubHost(url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Current.UpdateGitHubToken);
                request.Headers.Accept.ParseAdd("application/octet-stream");
            }
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            string text = await response.Content.ReadAsStringAsync(ct);
            // Accept "<hex>", "<hex> *file", or "<hex>  file" — take the first 64-hex-char run.
            foreach (var token in text.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries))
                if (token.Length == 64 && token.All(Uri.IsHexDigit))
                    return token;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }

    public bool Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return true; }
        catch (Exception) { return false; } // e.g. the user dismissed the UAC elevation prompt
    }

    private void ApplyGitHubHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("SystemCare-UpdateCheck");
        if (FeedUrl.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        // Only attach the access token when the feed is actually a GitHub host over HTTPS, so a custom
        // (or hijacked) feed URL can't be used to exfiltrate the user's token.
        if (!string.IsNullOrWhiteSpace(settings.Current.UpdateGitHubToken) && IsGitHubHost(FeedUrl))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Current.UpdateGitHubToken);
    }

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    /// <summary>True only for HTTPS GitHub hosts (api.github.com, github.com, *.githubusercontent.com).</summary>
    private static bool IsGitHubHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static string? GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
