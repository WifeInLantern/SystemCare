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

    // What the scheduled --run-maintenance pass actually does (defaults preserve the
    // original junk + RAM behaviour; the extra steps are opt-in).
    public bool MaintenanceCleanJunk { get; set; } = true;
    public bool MaintenanceTrimRam { get; set; } = true;
    public bool MaintenanceFlushDns { get; set; }
    public bool MaintenanceEmptyRecycleBin { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>Launch SystemCare minimized to the tray when Windows starts (via an elevated logon task).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Disables the animated backdrop and looping glows (accessibility / battery).</summary>
    public bool ReduceMotion { get; set; }

    // Live monitor (tray stats + floating mini-widget)
    /// <summary>Show live CPU/RAM in the system-tray icon and tooltip.</summary>
    public bool ShowTrayStats { get; set; }
    /// <summary>Show the always-on-top mini live-monitor widget.</summary>
    public bool ShowMiniMonitor { get; set; }
    public double? MiniMonitorLeft { get; set; }
    public double? MiniMonitorTop { get; set; }

    // Proactive resource alerts
    /// <summary>Toast + tray balloon when CPU/RAM/disk usage stays above its threshold for too long.</summary>
    public bool ResourceAlertsEnabled { get; set; }
    public int CpuAlertThresholdPercent { get; set; } = 90;
    public int RamAlertThresholdPercent { get; set; } = 90;
    public int DiskAlertThresholdPercent { get; set; } = 95;
    /// <summary>How long a metric must stay at or above its threshold before an alert fires.</summary>
    public int AlertSustainedMinutes { get; set; } = 5;

    // Safety
    public bool CreateRestorePointBeforeMaintenance { get; set; } = true;
    /// <summary>
    /// When a restore point would be created before a maintenance action, ask first (Yes/No) instead of
    /// creating it silently. Only applies when <see cref="CreateRestorePointBeforeMaintenance"/> is on.
    /// </summary>
    public bool AskBeforeBackup { get; set; } = true;
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

    /// <summary>
    /// Refuse to launch a downloaded update installer unless it carries a valid Authenticode signature.
    /// Default off because releases are not code-signed yet; a tampered/untrusted signature is rejected
    /// regardless. Turn on (and pin the publisher) once releases are signed.
    /// </summary>
    public bool RequireSignedUpdate { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>winget package Ids the user has chosen to skip in the Software Updater.</summary>
    public List<string> SoftwareUpdateExclusions { get; set; } = [];

    // Dashboard quick-actions (action ids shown as one-click tiles)
    public List<string> DashboardQuickActions { get; set; } =
        ["scanfix", "freeram", "flushdns", "emptybin", "restorepoint"];
}
