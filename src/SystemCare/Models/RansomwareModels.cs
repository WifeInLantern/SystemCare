namespace SystemCare.Models;

/// <summary>State of Windows Defender's Controlled Folder Access (the built-in ransomware shield).</summary>
public class RansomwareStatus
{
    public bool IsAvailable { get; init; }
    /// <summary>"Disabled", "Enabled", "AuditMode", or "Unknown".</summary>
    public string State { get; init; } = "Unknown";
    public bool IsOn => State.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
    /// <summary>User-added protected folders (the default system folders are protected implicitly).</summary>
    public List<string> ProtectedFolders { get; init; } = [];

    public string Headline => State switch
    {
        "Enabled" => "Ransomware protection is ON — untrusted apps can't modify your protected folders.",
        "AuditMode" => "Audit mode — blocks are logged but not enforced.",
        "Disabled" => "Ransomware protection is OFF.",
        _ => "Couldn't read ransomware protection status.",
    };
    public string Icon => IsOn ? "ShieldCheckmark24" : "Shield24";
}
