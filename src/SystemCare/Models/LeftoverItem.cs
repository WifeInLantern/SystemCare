using Microsoft.Win32;

namespace SystemCare.Models;

public enum LeftoverKind { Folder, Shortcut, RegistryKey, RegistryValue }

/// <summary>
/// A single remnant left behind after a program's own uninstaller has run: a folder, a shortcut,
/// or a registry key/value. Filesystem kinds carry <see cref="Path"/>; registry kinds carry the
/// hive/view/subkey/value and map 1:1 onto <see cref="RegistryIssue"/> so they flow through the
/// existing (backed-up) registry clean pipeline.
/// </summary>
public class LeftoverItem
{
    public required LeftoverKind Kind { get; init; }

    /// <summary>Full path for <see cref="LeftoverKind.Folder"/> / <see cref="LeftoverKind.Shortcut"/>.</summary>
    public string? Path { get; init; }

    // Registry kinds — mirror RegistryIssue.
    public RegistryHive Hive { get; init; }
    public RegistryView View { get; init; } = RegistryView.Registry64;
    public string? SubKeyPath { get; init; }
    /// <summary>Value to delete, or null to delete the whole subkey.</summary>
    public string? ValueName { get; init; }

    /// <summary>Folder size, measured during verification; 0 for shortcuts/registry.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Why this was flagged, e.g. "AppData folder", "Start Menu shortcut".</summary>
    public string Reason { get; init; } = "";

    public bool IsRegistry => Kind is LeftoverKind.RegistryKey or LeftoverKind.RegistryValue;

    /// <summary>Human-readable location for the UI.</summary>
    public string DisplayPath => IsRegistry ? ToRegistryIssue().DisplayPath : Path ?? "";

    /// <summary>Maps a registry-kind leftover onto a <see cref="RegistryIssue"/> for backup-then-delete.</summary>
    public RegistryIssue ToRegistryIssue() => new()
    {
        CategoryId = "leftover",
        CategoryName = "Uninstall leftover",
        Hive = Hive,
        View = View,
        SubKeyPath = SubKeyPath ?? "",
        ValueName = ValueName,
        Data = Reason,
        Reason = Reason,
    };
}

/// <summary>
/// Candidate leftovers captured <em>before</em> the uninstaller runs (while the app's registry data
/// is still present), plus the precomputed match tokens, so verification afterwards needn't re-derive
/// them.
/// </summary>
public class LeftoverPlan
{
    public required InstalledApp App { get; init; }
    public List<LeftoverItem> Candidates { get; init; } = [];
}

public class LeftoverRemoveResult
{
    public int FilesRemoved { get; set; }
    public int RegistryRemoved { get; set; }
    public int Skipped { get; set; }
    public long BytesRemoved { get; set; }
    public string RegistryBackupFolder { get; set; } = "";
}
