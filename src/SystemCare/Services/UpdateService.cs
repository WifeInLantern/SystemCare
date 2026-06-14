using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
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
    /// <summary>Launches a downloaded file (the installer).</summary>
    void Launch(string path);
}

public class UpdateService(ISettingsService settings) : IUpdateService
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
            string? assetApi = null, assetDownload = null, assetName = "";
            long assetSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                JsonElement? best = null;
                foreach (var a in assets.EnumerateArray())
                {
                    string n = GetStr(a, "name") ?? "";
                    if (n.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase)) { best = a; break; }
                    if (best is null && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) best = a;
                    best ??= a;
                }
                if (best is JsonElement chosen)
                {
                    assetApi = GetStr(chosen, "url");
                    assetDownload = GetStr(chosen, "browser_download_url");
                    assetName = GetStr(chosen, "name") ?? "SystemCare-Setup.exe";
                    assetSize = chosen.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                }
            }
            // A simple custom feed may put the download url under "url".
            if (assetDownload is null && assetApi is null)
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

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("SystemCare-UpdateCheck");
            if (hasToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Current.UpdateGitHubToken);
                request.Headers.Accept.ParseAdd("application/octet-stream");
            }

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            string path = Path.Combine(downloads, info.AssetName);

            long? total = response.Content.Headers.ContentLength ?? (info.AssetSize > 0 ? info.AssetSize : null);
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total is > 0) progress?.Report(read * 100.0 / total.Value);
            }
            progress?.Report(100);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private void ApplyGitHubHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("SystemCare-UpdateCheck");
        if (FeedUrl.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        if (!string.IsNullOrWhiteSpace(settings.Current.UpdateGitHubToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Current.UpdateGitHubToken);
    }

    private static string? GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
