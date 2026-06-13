namespace SystemCare.Models;

public enum PrivacyKind
{
    /// <summary>Delete a fixed set of files.</summary>
    Files,
    /// <summary>Delete directory contents recursively.</summary>
    DirectoryContents,
    /// <summary>Delete all values under a registry key.</summary>
    RegistryValues,
    /// <summary>Flush the DNS resolver cache.</summary>
    DnsCache,
    /// <summary>Clear the clipboard (UI thread).</summary>
    Clipboard,
}

public class PrivacyCategory
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public PrivacyKind Kind { get; init; } = PrivacyKind.Files;
    public bool EnabledByDefault { get; init; } = true;
    /// <summary>Browser process name (chrome/msedge/firefox) when the category is skipped while that browser runs.</summary>
    public string? BrowserProcess { get; init; }
    /// <summary>For Files: file paths. For DirectoryContents: directory paths. Resolved at scan time.</summary>
    public Func<IEnumerable<string>> GetPaths { get; init; } = () => [];
    /// <summary>For RegistryValues: the HKCU-relative key path.</summary>
    public string? RegistryKeyPath { get; init; }
}

public class PrivacyCategoryStatus
{
    public required PrivacyCategory Category { get; init; }
    public long Bytes { get; set; }
    public int ItemCount { get; set; }
    public bool BlockedByRunningBrowser { get; set; }
}

public class PrivacyCleanResult
{
    public long BytesRemoved { get; set; }
    public int ItemsRemoved { get; set; }
    public int ItemsSkipped { get; set; }
}
