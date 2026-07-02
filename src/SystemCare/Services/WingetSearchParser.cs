using SystemCare.Models;

namespace SystemCare.Services;

/// <summary>
/// Parses the fixed-width text table printed by <c>winget search</c> into <see cref="WingetSearchResult"/>
/// rows. Deliberately separate from <see cref="WingetUpgradeParser"/>/<see cref="WingetListParser"/>:
/// search output has no "Available" column, no footer count, and — uniquely — an optional "Match" column
/// (between Version and Source) that only appears when a result matched on a non-name field such as a
/// tag or moniker. Version's right edge must therefore resolve to Match when present, else Source.
/// </summary>
/// <remarks>
/// Like the other winget parsers, carriage returns are normalised to newlines (never stripped) so the
/// progress spinner winget fuses onto the header line becomes throwaway lines instead of shifting every
/// column offset (see <see cref="WingetUpgradeParser"/> remarks).
/// </remarks>
internal static class WingetSearchParser
{
    public static List<WingetSearchResult> Parse(string output, ILogService? log = null)
    {
        var list = new List<WingetSearchResult>();
        if (string.IsNullOrEmpty(output)) return list;
        if (output[0] == '﻿') output = output[1..]; // drop a stray UTF-8 BOM if present

        var lines = output.Replace("\r", "\n").Split('\n');

        int header = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains("Name") && lines[i].Contains("Id") && lines[i].Contains("Version")) { header = i; break; }
        if (header < 0)
        {
            // Includes "No package found matching input criteria." (no table, no warning).
            WarnIfTableLikely(output, list, log);
            return list;
        }

        string h = lines[header];
        int cId = h.IndexOf("Id", StringComparison.Ordinal);
        int cVer = h.IndexOf("Version", StringComparison.Ordinal);
        int cMatch = h.IndexOf("Match", StringComparison.Ordinal);   // optional
        int cSrc = h.IndexOf("Source", StringComparison.Ordinal);    // optional
        if (cId < 0 || cVer < 0) return list;

        int verEnd = cMatch >= 0 ? cMatch : (cSrc >= 0 ? cSrc : int.MaxValue);

        for (int i = header + 1; i < lines.Length; i++)
        {
            string l = lines[i];
            if (string.IsNullOrWhiteSpace(l) || l.StartsWith("---")) continue;

            string id = Slice(l, cId, cVer).Trim();
            string name = Slice(l, 0, cId).Trim();
            if (id.Length == 0 || name.Length == 0) continue;
            // winget clamps over-wide columns with an ellipsis; a truncated Id can't be installed
            // with --exact, so don't offer the row at all.
            if (id.Contains('…')) continue;

            list.Add(new WingetSearchResult
            {
                Name = name,
                Id = id,
                Version = Slice(l, cVer, verEnd).Trim(),
                Source = cSrc < 0 ? "" : Slice(l, cSrc, l.Length).Trim(),
            });
        }

        WarnIfTableLikely(output, list, log);
        return list;
    }

    private static void WarnIfTableLikely(string output, List<WingetSearchResult> list, ILogService? log)
    {
        if (list.Count == 0 && log is not null && output.Contains("---"))
            log.Warn("SoftwareHub", "winget search returned output but no results could be parsed (possible format/locale drift).");
    }

    private static string Slice(string s, int start, int end)
    {
        if (start < 0) start = 0;
        if (start >= s.Length) return "";
        if (end > s.Length) end = s.Length;
        return end <= start ? "" : s[start..end];
    }
}
