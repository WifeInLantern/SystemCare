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

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare");

    private string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Current { get; }

    public SettingsService()
    {
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // corrupted settings — fall back to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch (Exception)
        {
            // never crash over settings persistence
        }
    }
}
