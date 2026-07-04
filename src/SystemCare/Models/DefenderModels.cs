namespace SystemCare.Models;

/// <summary>Which Defender scan to launch. Values match MpCmdRun's -ScanType argument.</summary>
public enum DefenderScanType
{
    Quick = 1,
    Full = 2,
}

/// <summary>
/// A snapshot of Microsoft Defender's protection state, read from the
/// <c>MSFT_MpComputerStatus</c> WMI class (root\Microsoft\Windows\Defender).
/// </summary>
public class DefenderStatus
{
    /// <summary>False when Defender isn't the active antivirus (another AV manages protection).</summary>
    public bool IsAvailable { get; init; }

    public bool AntivirusEnabled { get; init; }
    public bool RealTimeProtectionEnabled { get; init; }
    public bool TamperProtectionEnabled { get; init; }

    public string AntivirusSignatureVersion { get; init; } = "";
    public int SignatureAgeDays { get; init; }

    public DateTime? LastQuickScan { get; init; }
    public DateTime? LastFullScan { get; init; }

    /// <summary>A short human-readable line summarising the overall posture.</summary>
    public string Headline { get; init; } = "";

    /// <summary>Icon + traffic-light colour hints reused by the page, matching the security page style.</summary>
    public string Icon => AntivirusEnabled && RealTimeProtectionEnabled ? "ShieldCheckmark24" : "Shield24";
}
