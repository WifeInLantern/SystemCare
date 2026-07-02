namespace SystemCare.Models;

/// <summary>One curated app offered for one-click install via winget. Display metadata only — the
/// actual install command is built from <see cref="Id"/> in <c>SoftwareHubService</c>.</summary>
public class SoftwareHubApp
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string Category { get; init; }
    public string Description { get; init; } = "";
}

/// <summary>A catalog app annotated with whether it's already installed, as returned by
/// <c>ISoftwareHubService.GetCatalogAsync</c> after cross-referencing <c>winget list</c>.</summary>
public class SoftwareHubAppStatus
{
    public required SoftwareHubApp App { get; init; }
    public bool IsInstalled { get; init; }
}

/// <summary>One row parsed from <c>winget search</c> output.</summary>
public class WingetSearchResult
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public string Version { get; init; } = "";
    public string Source { get; init; } = "";
}

public class SoftwareHubInstallProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string Name { get; init; } = "";
    public double Percent { get; init; }
}

public class SoftwareHubInstallResult
{
    public int Installed { get; init; }
    public int Failed { get; init; }
    public string Message { get; init; } = "";
}
