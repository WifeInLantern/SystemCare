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

    // Safety
    public bool CreateRestorePointBeforeMaintenance { get; set; } = true;
    public List<string> CleanupExclusions { get; set; } = [];
    public List<string> CustomJunkFolders { get; set; } = [];

    // Updates
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public string UpdateFeedUrl { get; set; } = "";
    public DateTime? LastUpdateCheckUtc { get; set; }

    // Dashboard quick-actions (action ids shown as one-click tiles)
    public List<string> DashboardQuickActions { get; set; } =
        ["scanfix", "freeram", "flushdns", "emptybin", "restorepoint"];
}
