namespace SystemCare.Services;

/// <summary>
/// Parses the fixed-width text table printed by <c>winget list</c> into the set of installed package
/// IDs. Deliberately separate from <see cref="WingetUpgradeParser"/>: that parser requires an
/// "Available" column to resolve and stops at upgrade-specific footer text, neither of which plain
/// <c>winget list</c> output reliably has. This parser only needs the Id column and only requires
/// Name/Id/Version headers, so it stays correct even when "Available"/"Source" are blank or absent.
/// </summary>
internal static class WingetListParser
{
    public static HashSet<string> ParseInstalledIds(string output, ILogService? log = null)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(output)) return ids;
        if (output[0] == '﻿') output = output[1..]; // drop a stray UTF-8 BOM if present

        // Normalise carriage returns to newlines in case winget ever spinners this table too (see
        // WingetUpgradeParser remarks — splitting, not stripping, keeps the real header at column 0).
        var lines = output.Replace("\r", "\n").Split('\n');

        int header = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains("Name") && lines[i].Contains("Id") && lines[i].Contains("Version")) { header = i; break; }
        if (header < 0) return ids;

        string h = lines[header];
        int cId = h.IndexOf("Id", StringComparison.Ordinal);
        int cVer = h.IndexOf("Version", StringComparison.Ordinal);
        if (cId < 0 || cVer < 0) return ids;

        for (int i = header + 1; i < lines.Length; i++)
        {
            string l = lines[i];
            if (string.IsNullOrWhiteSpace(l) || l.StartsWith("---")) continue;
            string id = Slice(l, cId, cVer).Trim();
            if (id.Length > 0) ids.Add(id);
        }

        if (ids.Count == 0 && log is not null && output.Contains("---"))
            log.Warn("SoftwareHub", "winget list returned output but no installed IDs could be parsed (possible format/locale drift).");

        return ids;
    }

    private static string Slice(string s, int start, int end)
    {
        if (start < 0) start = 0;
        if (start >= s.Length) return "";
        if (end > s.Length) end = s.Length;
        return end <= start ? "" : s[start..end];
    }
}
