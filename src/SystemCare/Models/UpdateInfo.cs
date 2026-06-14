namespace SystemCare.Models;

public class UpdateInfo
{
    public required string Version { get; init; }
    public string Notes { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    /// <summary>GitHub asset API url (used for authenticated/private downloads).</summary>
    public string? AssetApiUrl { get; init; }
    /// <summary>Public direct download url for the asset.</summary>
    public string? AssetDownloadUrl { get; init; }
    public string AssetName { get; init; } = "";
    public long AssetSize { get; init; }

    public bool HasAsset => !string.IsNullOrEmpty(AssetDownloadUrl) || !string.IsNullOrEmpty(AssetApiUrl);
}
