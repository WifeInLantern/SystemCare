using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IBootPerformanceService
{
    /// <summary>
    /// Last boot time + uptime (always available), plus boot duration and the slowest-starting apps from
    /// the Diagnostics-Performance event log when that log is enabled. Never throws.
    /// </summary>
    Task<BootPerformanceReport> GetAsync();
}

public class BootPerformanceService : IBootPerformanceService
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    public Task<BootPerformanceReport> GetAsync() => Task.Run(() =>
    {
        // These two are always available, with no special permissions or event log.
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        DateTime lastBootUtc = DateTime.UtcNow - uptime;

        int bootDurationMs = 0;
        var apps = new List<StartupImpact>();

        try
        {
            // Event 100 = boot performance summary (newest first).
            DateTime bootEventTimeUtc = default;
            var q100 = new EventLogQuery(LogName, PathType.LogName, "*[System[(EventID=100)]]") { ReverseDirection = true };
            using (var reader = new EventLogReader(q100))
            {
                using var ev = reader.ReadEvent();
                if (ev is not null)
                {
                    bootEventTimeUtc = ev.TimeCreated?.ToUniversalTime() ?? default;
                    var xml = XDocument.Parse(ev.ToXml());
                    bootDurationMs = ReadInt(xml, "BootTime");
                }
            }

            // Events 101 (app) / 103 (service) for the most recent boot only.
            if (bootEventTimeUtc != default)
            {
                var byName = new Dictionary<string, StartupImpact>(StringComparer.OrdinalIgnoreCase);
                DateTime cutoff = bootEventTimeUtc.AddMinutes(-2);

                foreach (int id in new[] { 101, 103 })
                {
                    var query = new EventLogQuery(LogName, PathType.LogName, $"*[System[(EventID={id})]]") { ReverseDirection = true };
                    using var reader = new EventLogReader(query);
                    int scanned = 0;
                    for (EventRecord? ev = reader.ReadEvent(); ev is not null && scanned < 300; ev = reader.ReadEvent())
                    {
                        using (ev)
                        {
                            scanned++;
                            var when = ev.TimeCreated?.ToUniversalTime() ?? default;
                            if (when != default && when < cutoff) break; // older than the last boot

                            var xml = XDocument.Parse(ev.ToXml());
                            string name = ReadStr(xml, "Name");
                            int total = ReadInt(xml, "TotalTime");
                            if (string.IsNullOrWhiteSpace(name) || total <= 0) continue;

                            if (!byName.TryGetValue(name, out var existing) || total > existing.DurationMs)
                                byName[name] = new StartupImpact { Name = name.Trim(), DurationMs = total, IsService = id == 103 };
                        }
                    }
                }

                apps = byName.Values.OrderByDescending(a => a.DurationMs).Take(15).ToList();
            }
        }
        catch (Exception)
        {
            // Diagnostics-Performance log disabled / access denied — fall back to last-boot + uptime only.
        }

        return new BootPerformanceReport
        {
            LastBootUtc = lastBootUtc,
            UptimeText = FormatUptime(uptime),
            BootDurationMs = bootDurationMs,
            Apps = apps,
        };
    });

    private static string FormatUptime(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
        return $"{t.Minutes}m";
    }

    // ---- event XML helpers (the event uses the standard Event namespace) ----

    private static int ReadInt(XDocument doc, string dataName)
    {
        string raw = ReadStr(doc, dataName);
        return int.TryParse(raw, out int v) ? v : 0;
    }

    private static string ReadStr(XDocument doc, string dataName)
    {
        var root = doc.Root;
        if (root is null) return "";
        XNamespace ns = root.GetDefaultNamespace();
        var data = root.Descendants(ns + "Data")
            .FirstOrDefault(d => (string?)d.Attribute("Name") == dataName);
        return data?.Value ?? "";
    }
}
