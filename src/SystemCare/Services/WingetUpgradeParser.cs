using System.Text.RegularExpressions;
using SystemCare.Models;

namespace SystemCare.Services;

/// <summary>
/// Parses the fixed-width text table printed by <c>winget upgrade</c> into <see cref="SoftwareUpdate"/> rows.
/// </summary>
/// <remarks>
/// winget renders a source-update progress spinner using carriage returns (<c>\r  - \r … \r</c>) on the same
/// physical line as the <c>Name … Id … Available</c> header. We therefore normalise <c>\r</c> to <c>\n</c>
/// (turning each spinner frame into a throwaway line) BEFORE locating the header — deleting <c>\r</c> instead
/// would fuse the spinner text in front of <c>Name</c>, pushing every column offset past the end of the data
/// rows so that nothing parsed and the page silently reported "all apps up to date".
/// </remarks>
internal static class WingetUpgradeParser
{
    public static List<SoftwareUpdate> Parse(string output, ILogService? log = null)
    {
        var list = new List<SoftwareUpdate>();
        if (string.IsNullOrEmpty(output)) return list;
        if (output[0] == '﻿') output = output[1..]; // drop a stray UTF-8 BOM if present

        // Normalise carriage returns to newlines (NOT ""): keeps the real header clean at column 0.
        var lines = output.Replace("\r", "\n").Split('\n');

        int header = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains("Name") && lines[i].Contains("Id") && lines[i].Contains("Available")) { header = i; break; }
        if (header < 0)
        {
            WarnIfTableLikely(output, list, log);
            return list;
        }

        string h = lines[header];
        int cId = h.IndexOf("Id", StringComparison.Ordinal);
        int cVer = h.IndexOf("Version", StringComparison.Ordinal);
        int cAvail = h.IndexOf("Available", StringComparison.Ordinal);
        int cSrc = h.IndexOf("Source", StringComparison.Ordinal);
        if (cId < 0 || cVer < 0 || cAvail < 0) return list;

        for (int i = header + 1; i < lines.Length; i++)
        {
            string l = lines[i];
            if (string.IsNullOrWhiteSpace(l)) continue;
            if (l.StartsWith("---")) continue;
            // footer, e.g. "9 upgrades available." — stops before any secondary "explicit targeting" table.
            if (l.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(l, @"package\(s\) have", RegexOptions.IgnoreCase)) break;

            string id = Slice(l, cId, cVer).Trim();
            string name = Slice(l, 0, cId).Trim();
            if (id.Length == 0 || name.Length == 0) continue;

            list.Add(new SoftwareUpdate
            {
                Name = name,
                Id = id,
                CurrentVersion = Slice(l, cVer, cAvail).Trim(),
                AvailableVersion = Slice(l, cAvail, cSrc < 0 ? l.Length : cSrc).Trim(),
                Source = cSrc < 0 ? "" : Slice(l, cSrc, l.Length).Trim(),
            });
        }

        WarnIfTableLikely(output, list, log);
        return list;
    }

    // If winget clearly printed a table (separator dashes or a footer count) but we extracted nothing, the
    // output format probably drifted or is localised. Log it so the failure is diagnosable, not silent.
    private static void WarnIfTableLikely(string output, List<SoftwareUpdate> list, ILogService? log)
    {
        if (list.Count > 0 || log is null) return;
        if (output.Contains("---") || output.Contains("upgrades available", StringComparison.OrdinalIgnoreCase))
            log.Warn("SoftwareUpdate", "winget returned output but no updates could be parsed (possible format/locale drift).");
    }

    private static string Slice(string s, int start, int end)
    {
        if (start < 0) start = 0;
        if (start >= s.Length) return "";
        if (end > s.Length) end = s.Length;
        return end <= start ? "" : s[start..end];
    }
}
