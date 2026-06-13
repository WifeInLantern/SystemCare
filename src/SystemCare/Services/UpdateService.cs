using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    /// <summary>The newer release found by the last check, or null.</summary>
    UpdateInfo? LatestAvailable { get; }
    /// <summary>Returns a newer release if one is available, else null. Never throws.</summary>
    Task<UpdateInfo?> CheckAsync();
    void OpenDownload();
}

public class UpdateService(ISettingsService settings) : IUpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public UpdateInfo? LatestAvailable { get; private set; }

    public async Task<UpdateInfo?> CheckAsync()
    {
        settings.Current.LastUpdateCheckUtc = DateTime.UtcNow;
        settings.Save();

        string url = settings.Current.UpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(url)) return null; // no feed configured → nothing to check

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("SystemCare-UpdateCheck");
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Accept a simple {version,url,notes} feed or a GitHub releases/latest object.
            string version = (GetStr(root, "version") ?? GetStr(root, "tag_name") ?? "").TrimStart('v', 'V');
            string download = GetStr(root, "url") ?? GetStr(root, "html_url") ?? "";
            string notes = GetStr(root, "notes") ?? GetStr(root, "body") ?? "";

            if (Version.TryParse(version, out var remote) &&
                Version.TryParse(CurrentVersion, out var current) &&
                remote > current)
            {
                LatestAvailable = new UpdateInfo { Version = version, DownloadUrl = download, Notes = notes };
                return LatestAvailable;
            }
        }
        catch (Exception)
        {
            // offline / bad feed / parse error — treat as "no update"
        }

        LatestAvailable = null;
        return null;
    }

    public void OpenDownload()
    {
        var url = LatestAvailable?.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private static string? GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
