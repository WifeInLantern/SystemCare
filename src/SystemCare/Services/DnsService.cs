using System.Net.NetworkInformation;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IDnsService
{
    /// <summary>Built-in resolver presets (Automatic, Cloudflare, Google, Quad9, OpenDNS).</summary>
    IReadOnlyList<DnsProvider> Providers { get; }
    /// <summary>Names (aliases) of the currently-connected, non-virtual network adapters.</summary>
    List<string> GetActiveAdapters();
    /// <summary>Reads the DNS servers currently configured on an adapter, comma-joined.</summary>
    string GetCurrentDns(string adapter);
    /// <summary>Applies a preset (or reverts to DHCP) on an adapter via netsh. Returns (ok, message).</summary>
    Task<(bool Ok, string Message)> ApplyAsync(string adapter, DnsProvider provider, CancellationToken ct);
}

public class DnsService : IDnsService
{
    private readonly ILogService _log;

    public DnsService(ILogService log) => _log = log;

    public IReadOnlyList<DnsProvider> Providers { get; } =
    [
        new() { Name = "Automatic (DHCP)", Description = "Use whatever DNS your router or ISP hands out." },
        new() { Name = "Cloudflare", Description = "Fast, privacy-first (1.1.1.1).", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
        new() { Name = "Google Public DNS", Description = "Reliable, globally distributed.", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
        new() { Name = "Quad9", Description = "Blocks known malicious domains.", Primary = "9.9.9.9", Secondary = "149.112.112.112" },
        new() { Name = "OpenDNS", Description = "Optional content filtering.", Primary = "208.67.222.222", Secondary = "208.67.220.220" },
    ];

    public List<string> GetActiveAdapters()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                // Skip virtual/host-only adapters that can't carry real DNS changes.
                if (ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(ni.Name);
            }
        }
        catch (Exception ex) { _log.Warn("DNS", $"Adapter enumeration failed: {ex.Message}"); }
        return list;
    }

    public string GetCurrentDns(string adapter)
    {
        try
        {
            var ni = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, adapter, StringComparison.OrdinalIgnoreCase));
            if (ni is null) return "—";
            var dns = ni.GetIPProperties().DnsAddresses
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();
            return dns.Count == 0 ? "Automatic (DHCP)" : string.Join(", ", dns);
        }
        catch (Exception) { return "—"; }
    }

    public async Task<(bool Ok, string Message)> ApplyAsync(string adapter, DnsProvider provider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(adapter)) return (false, "No network adapter selected.");

        try
        {
            if (provider.IsAutomatic)
            {
                var (code, output) = await ProcessRunner.RunAsync(
                    "netsh", $"interface ipv4 set dnsservers name=\"{adapter}\" source=dhcp", ct);
                return Result(code, output, $"{adapter} reverted to automatic (DHCP) DNS.");
            }

            var (c1, o1) = await ProcessRunner.RunAsync(
                "netsh", $"interface ipv4 set dnsservers name=\"{adapter}\" static {provider.Primary} primary", ct);
            if (c1 != 0) return Result(c1, o1, "");

            var (c2, o2) = await ProcessRunner.RunAsync(
                "netsh", $"interface ipv4 add dnsservers name=\"{adapter}\" {provider.Secondary} index=2", ct);
            // A failed secondary isn't fatal — the primary is already set.
            if (c2 != 0) _log.Warn("DNS", $"Secondary DNS not set: {o2}");

            _log.Info("DNS", $"{adapter} DNS set to {provider.Name} ({provider.Primary}/{provider.Secondary}).");
            return (true, $"{adapter} now uses {provider.Name} ({provider.Primary}, {provider.Secondary}).");
        }
        catch (OperationCanceledException) { return (false, "Cancelled."); }
        catch (Exception ex)
        {
            _log.Error("DNS", "Failed to apply DNS", ex);
            return (false, ex.Message);
        }
    }

    private (bool, string) Result(int code, string output, string okMessage)
    {
        if (code == 0) { _log.Info("DNS", okMessage); return (true, okMessage); }
        string msg = string.IsNullOrWhiteSpace(output) ? $"netsh exited with code {code}." : output.Trim();
        _log.Warn("DNS", msg);
        return (false, msg);
    }
}
