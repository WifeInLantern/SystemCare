namespace SystemCare.Models;

public class AppPackage
{
    public required string Name { get; init; }
    public required string PackageFullName { get; init; }
    public string Publisher { get; init; } = "";
    public bool IsBloatware { get; init; }
    /// <summary>Friendly short name (last segment of the package name).</summary>
    public string DisplayName
    {
        get
        {
            int dot = Name.LastIndexOf('.');
            return dot >= 0 && dot < Name.Length - 1 ? Name[(dot + 1)..] : Name;
        }
    }
}
