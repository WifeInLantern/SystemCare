namespace SystemCare.Models;

/// <summary>A detected browser and the size of its cache, for the Browser Cleanup page.</summary>
public class BrowserInfo
{
    public required string Name { get; init; }
    /// <summary>"Chromium" or "Firefox" — determines where cache/cookies/history live.</summary>
    public required string Kind { get; init; }
    /// <summary>Root user-data folder (Chromium "User Data" or Firefox roaming profiles root).</summary>
    public required string UserDataPath { get; init; }
    /// <summary>LocalAppData profiles root, used by Firefox for its cache2 folder.</summary>
    public string LocalDataPath { get; init; } = "";
    public long CacheBytes { get; set; }
}
