using SystemCare.Helpers;

namespace SystemCare.Services;

public sealed class WifiConnectionInfo
{
    public string Ssid { get; init; } = "";
    public int SignalPercent { get; init; }
    public int Channel { get; init; }
    public string Band { get; init; } = "";
    public string RadioType { get; init; } = "";
    public double ReceiveMbps { get; init; }
    public double TransmitMbps { get; init; }
}

public sealed class WifiNetworkInfo
{
    public string Ssid { get; init; } = "";
    public int SignalPercent { get; init; }
    public int Channel { get; init; }
    public string Band { get; init; } = "";
}

public sealed class WifiReport
{
    public bool WlanAvailable { get; init; }
    public WifiConnectionInfo? Connection { get; init; }
    public IReadOnlyList<WifiNetworkInfo> Nearby { get; init; } = [];
    /// <summary>Networks (incl. ours) sharing the connected channel — a congestion hint.</summary>
    public int SameChannelCount { get; init; }
}

public interface IWifiInfoService
{
    Task<WifiReport> GetReportAsync(CancellationToken ct);
}

/// <summary>
/// Wi-Fi Analyzer (2.17): current connection quality (signal, channel, band, PHY rates) and the
/// nearby-network landscape, parsed from <c>netsh wlan</c>. Parsing keys are the English netsh
/// labels; on non-English Windows the parser degrades gracefully to "no data" rather than guessing.
/// Read-only.
/// </summary>
public sealed class WifiInfoService(ILogService log) : IWifiInfoService
{
    public Task<WifiReport> GetReportAsync(CancellationToken ct) => Task.Run(async () =>
    {
        try
        {
            var (exitIf, outIf) = await ProcessRunner.RunAsync("netsh.exe", "wlan show interfaces", ct);
            if (exitIf != 0 || outIf.Contains("is not running", StringComparison.OrdinalIgnoreCase))
                return new WifiReport { WlanAvailable = false };

            var connection = ParseInterface(outIf);

            var (exitNet, outNet) = await ProcessRunner.RunAsync("netsh.exe", "wlan show networks mode=bssid", ct);
            var nearby = exitNet == 0 ? ParseNetworks(outNet) : [];

            int sameChannel = connection is { Channel: > 0 }
                ? nearby.Count(n => n.Channel == connection.Channel && !string.Equals(n.Ssid, connection.Ssid, StringComparison.Ordinal)) + 1
                : 0;

            return new WifiReport
            {
                WlanAvailable = true,
                Connection = connection,
                Nearby = nearby.OrderByDescending(n => n.SignalPercent).ToList(),
                SameChannelCount = sameChannel,
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Warn("WiFi", $"netsh parse failed: {ex.Message}");
            return new WifiReport { WlanAvailable = false };
        }
    }, ct);

    private static WifiConnectionInfo? ParseInterface(string output)
    {
        var fields = ParseKeyValues(output);
        string ssid = Get(fields, "SSID");
        if (string.IsNullOrEmpty(ssid)) return null; // not connected (or localized output)

        int channel = ParseLeadingInt(Get(fields, "Channel"));
        return new WifiConnectionInfo
        {
            Ssid = ssid,
            SignalPercent = ParseLeadingInt(Get(fields, "Signal")),
            Channel = channel,
            Band = Get(fields, "Band") is { Length: > 0 } b ? b : GuessBand(channel),
            RadioType = Get(fields, "Radio type"),
            ReceiveMbps = ParseLeadingDouble(Get(fields, "Receive rate (Mbps)")),
            TransmitMbps = ParseLeadingDouble(Get(fields, "Transmit rate (Mbps)")),
        };
    }

    private static List<WifiNetworkInfo> ParseNetworks(string output)
    {
        var result = new List<WifiNetworkInfo>();
        string? ssid = null;
        int bestSignal = 0, channel = 0;
        string band = "";

        void Flush()
        {
            if (!string.IsNullOrEmpty(ssid) && bestSignal > 0)
                result.Add(new WifiNetworkInfo
                {
                    Ssid = ssid!,
                    SignalPercent = bestSignal,
                    Channel = channel,
                    Band = band.Length > 0 ? band : GuessBand(channel),
                });
            bestSignal = 0; channel = 0; band = "";
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd();
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            if (key.StartsWith("SSID", StringComparison.Ordinal) && !key.StartsWith("SSID BSSID", StringComparison.Ordinal))
            {
                Flush();
                ssid = value.Length > 0 ? value : "(hidden network)";
            }
            else if (key.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
            {
                bestSignal = Math.Max(bestSignal, ParseLeadingInt(value));
            }
            else if (key.StartsWith("Channel", StringComparison.OrdinalIgnoreCase) && channel == 0)
            {
                channel = ParseLeadingInt(value);
            }
            else if (key.StartsWith("Band", StringComparison.OrdinalIgnoreCase) && band.Length == 0)
            {
                band = value;
            }
        }
        Flush();
        return result;
    }

    /// <summary>First occurrence of each "Key : Value" line (the interface output has one block).</summary>
    private static Dictionary<string, string> ParseKeyValues(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0) continue;
            string key = raw[..colon].Trim();
            string value = raw[(colon + 1)..].Trim();
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = value;
        }
        return map;
    }

    private static string Get(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) ? v : "";

    private static int ParseLeadingInt(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int result) ? result : 0;
    }

    private static double ParseLeadingDouble(string value)
    {
        var chars = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(chars, System.Globalization.CultureInfo.InvariantCulture, out double result) ? result : 0;
    }

    private static string GuessBand(int channel) => channel switch
    {
        >= 1 and <= 14 => "2.4 GHz",
        >= 32 and <= 177 => "5 GHz",
        > 177 => "6 GHz",
        _ => "",
    };
}
