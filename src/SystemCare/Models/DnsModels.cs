namespace SystemCare.Models;

/// <summary>A selectable DNS resolver preset for the Secure DNS switcher.</summary>
public class DnsProvider
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Empty for the "Automatic (DHCP)" preset, which reverts the adapter to DHCP-assigned DNS.</summary>
    public string Primary { get; init; } = "";
    public string Secondary { get; init; } = "";
    public bool IsAutomatic => string.IsNullOrEmpty(Primary);
    public string ServersText => IsAutomatic ? "From your router / ISP" : $"{Primary}  •  {Secondary}";
}
