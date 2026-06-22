using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
    string SettingsDirectory { get; }
}

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _saveGate = new();

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare");

    private string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Current { get; }

    public SettingsService()
    {
        Current = Load(out bool migratedLegacyToken);
        // Rewrite immediately so a migrated plaintext token is replaced on disk by its encrypted form.
        if (migratedLegacyToken) Save();
    }

    private AppSettings Load(out bool migratedLegacyToken)
    {
        migratedLegacyToken = false;
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                migratedLegacyToken = MigrateLegacyToken(settings, json);
                return settings;
            }
        }
        catch (Exception)
        {
            // corrupted settings — fall back to defaults
        }
        return new AppSettings();
    }

    /// <summary>
    /// Pre-1.10 settings stored the GitHub token in clear under "UpdateGitHubToken". If we find one and no
    /// encrypted token exists yet, move it into the DPAPI-protected field (the setter encrypts it) so the
    /// next save rewrites the file without the plaintext. Returns true if a migration happened.
    /// </summary>
    private static bool MigrateLegacyToken(AppSettings settings, string rawJson)
    {
        if (!string.IsNullOrEmpty(settings.GitHubTokenProtected)) return false;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("UpdateGitHubToken", out var legacy) &&
                legacy.ValueKind == JsonValueKind.String)
            {
                string token = legacy.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(token))
                {
                    settings.UpdateGitHubToken = token; // setter encrypts into GitHubTokenProtected
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // malformed legacy field — nothing to migrate
        }
        return false;
    }

    public void Save()
    {
        // The settings singleton is written from several threads (UI toggles, background scans,
        // scheduled maintenance). Serialize writes and use a per-call temp file so two concurrent
        // saves can't clobber each other's temp and fail the File.Move (which silently lost a write).
        lock (_saveGate)
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                string tmp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
                File.Move(tmp, SettingsPath, overwrite: true);
            }
            catch (Exception)
            {
                // never crash over settings persistence
            }
        }
    }
}
