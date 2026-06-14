namespace SystemCare.Models;

/// <summary>An installed device + its current driver (from Win32_PnPSignedDriver),
/// with a problem flag from Win32_PnPEntity.ConfigManagerErrorCode.</summary>
public class DriverDevice
{
    public required string Name { get; init; }
    public string DeviceClass { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public string DeviceId { get; init; } = "";
    public bool HasProblem { get; init; }
    public string ProblemText { get; init; } = "";

    public string DriverDateText => DriverDate?.ToString("yyyy-MM-dd") ?? "—";
    public string VersionText =>
        string.IsNullOrWhiteSpace(DriverVersion) ? DriverDateText : $"v{DriverVersion} · {DriverDateText}";
}

/// <summary>An available driver update found via the Windows Update Agent.</summary>
public class DriverUpdate
{
    public required string Title { get; init; }
    public string Manufacturer { get; init; } = "";
    public string DriverClass { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public long SizeBytes { get; init; }
    public string UpdateId { get; init; } = "";
    /// <summary>Index into the service's cached search results (used to install).</summary>
    public int Index { get; init; }

    public string DateText => DriverDate?.ToString("yyyy-MM-dd") ?? "";
    public string SizeText => SizeBytes > 0 ? Helpers.ByteFormatter.Format(SizeBytes) : "";
}

public class DriverInstallProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string Title { get; init; } = "";
    public double Percent { get; init; }
}

public class DriverInstallResult
{
    public int Installed { get; init; }
    public int Failed { get; init; }
    public bool RebootRequired { get; init; }
    public string Message { get; init; } = "";
}
