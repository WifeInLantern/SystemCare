using System.Text.Json.Serialization;
using SystemCare.Helpers;

namespace SystemCare.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public Dictionary<string, bool> JunkCategoryToggles { get; set; } = [];
    public int SkipTempNewerThanHours { get; set; } = 24;
    public int LargeFileMinMB { get; set; } = 100;
    public int LargeFileTopN { get; set; } = 50;
    public int DupMinSizeMB { get; set; } = 1;
    public bool ShowSystemTasks { get; set; }
    public DateTime? LastScanUtc { get; set; }
    public int? LastHealthScore { get; set; }

    // Auto-maintenance + tray
    public bool AutoMaintenanceEnabled { get; set; }
    public string MaintenanceFrequency { get; set; } = "Weekly"; // Daily | Weekly
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>Launch SystemCare minimized to the tray when Windows starts (via an elevated logon task).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Disables the animated backdrop and looping glows (accessibility / battery).</summary>
    public bool ReduceMotion { get; set; }

    // Safety
    public bool CreateRestorePointBeforeMaintenance { get; set; } = true;
    public List<string> CleanupExclusions { get; set; } = [];
    public List<string> CustomJunkFolders { get; set; } = [];

    // Updates
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    /// <summary>GitHub releases API (or a custom JSON feed). Empty = use the built-in default repo.</summary>
    public string UpdateFeedUrl { get; set; } = "";
    /// <summary>DPAPI-encrypted (CurrentUser) GitHub token blob — the only token form ever written to disk.</summary>
    public string? GitHubTokenProtected { get; set; }

    /// <summary>
    /// Optional GitHub token so the updater can read a PRIVATE repo's releases/assets. Encrypted at rest
    /// via DPAPI (see <see cref="GitHubTokenProtected"/>); never serialized in clear. Setting it to an
    /// empty string clears the stored token.
    /// </summary>
    [JsonIgnore]
    public string UpdateGitHubToken
    {
        get => DataProtection.Unprotect(GitHubTokenProtected);
        set => GitHubTokenProtected = DataProtection.Protect(value);
    }
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>winget package Ids the user has chosen to skip in the Software Updater.</summary>
    public List<string> SoftwareUpdateExclusions { get; set; } = [];

    // Dashboard quick-actions (action ids shown as one-click tiles)
    public List<string> DashboardQuickActions { get; set; } =
        ["scanfix", "freeram", "flushdns", "emptybin", "restorepoint"];
}
