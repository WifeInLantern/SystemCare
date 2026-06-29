using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IReliabilityService
{
    /// <summary>Scans the local Event Log for reliability problems in the last <paramref name="days"/> days.</summary>
    Task<ReliabilityReport> GetAsync(int days);
}

/// <summary>
/// Reads the Windows Event Log (System + Application) and classifies recent failures — blue screens,
/// unexpected shutdowns, app crashes/hangs, disk errors and service failures — into a stability report.
/// Reads the local log only (nothing leaves the PC), runs off the UI thread, and never throws. Mirrors the
/// EventLogReader/XPath approach in <see cref="BootPerformanceService"/>.
/// </summary>
public sealed class ReliabilityService : IReliabilityService
{
    private const int ScanCapPerLog = 500;
    private const int KeepMax = 200;

    public Task<ReliabilityReport> GetAsync(int days) => Task.Run(() =>
    {
        long windowMs = (long)days * 24 * 60 * 60 * 1000;
        var events = new List<ReliabilityEvent>();

        bool readSystem = Scan("System",
            $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {windowMs}]]]", events, isSystem: true);
        bool readApp = Scan("Application",
            $"*[System[(Level=2) and TimeCreated[timediff(@SystemTime) <= {windowMs}]]]", events, isSystem: false);

        events.Sort((a, b) => b.TimeUtc.CompareTo(a.TimeUtc));
        int score = ReliabilityScore.Score(events);
        var kept = events.Count > KeepMax ? events.Take(KeepMax).ToList() : events;

        return new ReliabilityReport
        {
            Events = kept,
            Score = score,
            DaysAnalyzed = days,
            Read = readSystem || readApp,
        };
    });

    private static bool Scan(string log, string xpath, List<ReliabilityEvent> into, bool isSystem)
    {
        EventLogReader reader;
        try
        {
            reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true });
        }
        catch (Exception)
        {
            return false; // log unavailable / access denied
        }

        using (reader)
        {
            int scanned = 0;
            while (scanned < ScanCapPerLog)
            {
                EventRecord? ev;
                try { ev = reader.ReadEvent(); }
                catch (Exception) { break; }
                if (ev is null) break;

                using (ev)
                {
                    scanned++;
                    try
                    {
                        string source = ev.ProviderName ?? "";
                        int id = ev.Id;
                        var hit = isSystem ? ClassifySystem(source, id) : ClassifyApp(source, id);
                        if (hit is { } c)
                        {
                            var when = ev.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow;
                            into.Add(new ReliabilityEvent(c.Item1, c.Item2, source, TitleFor(c.Item1, ev, source), when));
                        }
                    }
                    catch (Exception) { /* skip a malformed event */ }
                }
            }
        }
        return true;
    }

    private static (ReliabilityCategory, ReliabilitySeverity)? ClassifySystem(string source, int id)
    {
        if (source == "Microsoft-Windows-Kernel-Power" && id == 41) return (ReliabilityCategory.UnexpectedShutdown, ReliabilitySeverity.Critical);
        if (source == "EventLog" && id == 6008) return (ReliabilityCategory.UnexpectedShutdown, ReliabilitySeverity.Error);
        if (id == 1001 && (source == "Microsoft-Windows-WER-SystemErrorReporting" || source == "BugCheck"))
            return (ReliabilityCategory.BlueScreen, ReliabilitySeverity.Critical);
        if (source == "Service Control Manager" && id is 7000 or 7001 or 7022 or 7023 or 7024 or 7026 or 7031 or 7034)
            return (ReliabilityCategory.ServiceFailure, ReliabilitySeverity.Error);
        if (IsDiskSource(source)) return (ReliabilityCategory.DiskError, ReliabilitySeverity.Error);
        return null;
    }

    private static (ReliabilityCategory, ReliabilitySeverity)? ClassifyApp(string source, int id)
    {
        if (source == "Application Error" && id == 1000) return (ReliabilityCategory.Crash, ReliabilitySeverity.Error);
        if (source == ".NET Runtime" && id == 1026) return (ReliabilityCategory.Crash, ReliabilitySeverity.Error);
        if (source == "Application Hang" && id == 1002) return (ReliabilityCategory.AppHang, ReliabilitySeverity.Warning);
        return null;
    }

    private static bool IsDiskSource(string s) =>
        s.Contains("disk", StringComparison.OrdinalIgnoreCase)
        || s.Contains("ntfs", StringComparison.OrdinalIgnoreCase)
        || s is "volmgr" or "storahci" or "stornvme" or "nvme" or "iaStorA";

    private static string TitleFor(ReliabilityCategory cat, EventRecord ev, string source) => cat switch
    {
        ReliabilityCategory.BlueScreen => "Blue screen (system crash)",
        ReliabilityCategory.UnexpectedShutdown => "Unexpected shutdown or restart",
        ReliabilityCategory.DiskError => $"Disk error · {source}",
        ReliabilityCategory.ServiceFailure => "A service failed to start or stopped unexpectedly",
        ReliabilityCategory.Crash => FirstData(ev) is { Length: > 0 } app ? $"App crash · {app}" : "Application crash",
        ReliabilityCategory.AppHang => FirstData(ev) is { Length: > 0 } app ? $"App stopped responding · {app}" : "Application stopped responding",
        _ => source,
    };

    // The first positional <Data> of an Application Error/Hang event is the faulting app's name.
    private static string FirstData(EventRecord ev)
    {
        try
        {
            var root = XDocument.Parse(ev.ToXml()).Root;
            if (root is null) return "";
            XNamespace ns = root.GetDefaultNamespace();
            return root.Descendants(ns + "Data").FirstOrDefault()?.Value?.Trim() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }
}
