namespace SystemCare.Tests;

/// <summary>
/// Real <c>winget upgrade</c> output captured verbatim for parser/service tests. Includes the carriage-return
/// progress spinner winget fuses onto the header line — the exact shape that previously broke parsing.
/// </summary>
internal static class WingetTestSamples
{
    // Captured from `winget upgrade --include-unknown --disable-interactivity` (winget 1.28). 8 real updates.
    public const string EightUpgradesWithSpinner =
        "\r   - \r                                                                                                                        \rName                           Id                                Version     Available  Source\r\n" +
        "----------------------------------------------------------------------------------------------\n" +
        "MuseHub                        Muse.MuseHub                      2.8.1.2171  2.8.2.2197 winget\n" +
        "WinRAR 7.22 (64-bit)           RARLab.WinRAR                     7.22.0      7.23.0     winget\n" +
        "LM Studio 0.4.16+2             ElementLabs.LMStudio              0.4.16+2    0.4.18+1   winget\n" +
        "Visual Studio Build Tools 2026 Microsoft.VisualStudio.BuildTools 18.7.1      18.7.2     winget\n" +
        "Roblox Player for Admin        Roblox.Roblox                     Unknown     0.726      winget\n" +
        "Ollama version 0.30.9          Ollama.Ollama                     0.30.9      0.30.11    winget\n" +
        "Claude                         Anthropic.Claude                  1.14271.0.0 1.15962.1  winget\n" +
        "Windows Subsystem for Linux    Microsoft.WSL                     2.7.8.0     2.7.10     winget\n" +
        "8 upgrades available.\n";

    /// <summary>
    /// Synthetic <c>winget list --accept-source-agreements --disable-interactivity</c> output. Modelled on
    /// real output's fixed-width layout, including rows with a blank Available column (nothing to upgrade to)
    /// and a blank Id/Source (an ARP-only entry winget can't map to a package source) — both of which
    /// <c>WingetUpgradeParser</c> would mishandle (it requires a resolvable "Available" column) but
    /// <c>WingetListParser</c> must tolerate since it only needs Name/Id/Version.
    /// </summary>
    public static readonly string InstalledAppsListSample = BuildInstalledAppsListSample();

    /// <summary>
    /// Synthetic <c>winget search</c> output with the carriage-return spinner fused onto the header line
    /// AND the optional "Match" column (appears when a result matched on a tag/moniker instead of its
    /// name). Version's right edge must resolve to Match, not Source, or the version strings swallow the
    /// match text. Includes a row whose Id was clamped with an ellipsis (uninstallable with --exact) and
    /// an ARP-style row with a blank Id.
    /// </summary>
    public static readonly string SearchResultsWithSpinnerAndMatch = BuildSearchSample(includeMatch: true);

    /// <summary>The same results without the optional Match column (name-only matches).</summary>
    public static readonly string SearchResultsWithoutMatch = BuildSearchSample(includeMatch: false);

    public const string SearchNoResults = "No package found matching input criteria.\n";

    private static string BuildSearchSample(bool includeMatch)
    {
        (string Name, string Id, string Version, string Match)[] rows =
        [
            ("Visual Studio Code", "Microsoft.VisualStudioCode", "1.90.0", ""),
            ("Notepad++", "Notepad++.Notepad++", "8.6.9", "Tag: editor"),
            ("VLC media player", "VideoLAN.VLC", "3.0.20", "Moniker: vlc"),
            ("Some Very Long Publisher App", "Publisher.WithAnExtremelyLon…", "1.0", ""),
            ("Orphaned ARP Entry", "", "1.0.0", ""),
        ];

        static string Col(string s, int w) => s.PadRight(w);
        var sb = new System.Text.StringBuilder();
        sb.Append("\r   - \r                                                                        \r");
        sb.Append(Col("Name", 30)).Append(Col("Id", 32)).Append(Col("Version", 11));
        if (includeMatch) sb.Append(Col("Match", 16));
        sb.Append("Source\n");
        sb.Append(new string('-', includeMatch ? 95 : 79)).Append('\n');
        foreach (var r in rows)
        {
            sb.Append(Col(r.Name, 30)).Append(Col(r.Id, 32)).Append(Col(r.Version, 11));
            if (includeMatch) sb.Append(Col(r.Match, 16));
            sb.Append("winget\n");
        }
        return sb.ToString();
    }

    private static string BuildInstalledAppsListSample()
    {
        (string Name, string Id, string Version)[] rows =
        [
            ("7-Zip 22.01 (x64)", "7zip.7zip", "22.01.00.0"),
            ("Git", "Git.Git", "2.44.0.2"),
            ("VLC media player", "VideoLAN.VLC", "3.0.20"),
            ("WinRAR 7.22 (64-bit)", "RARLab.WinRAR", "7.22.0"),
            ("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft.VCRedist.2015+.x64", "14.38.33135.0"),
            ("Realtek Audio Console", "", "1.0.0.0"),
        ];

        static string Col(string s, int w) => s.PadRight(w);
        var sb = new System.Text.StringBuilder();
        sb.Append(Col("Name", 55)).Append(Col("Id", 32)).Append(Col("Version", 14)).Append(Col("Available", 11)).Append("Source\n");
        sb.Append(new string('-', 120)).Append('\n');
        foreach (var r in rows)
            sb.Append(Col(r.Name, 55)).Append(Col(r.Id, 32)).Append(r.Version).Append('\n');
        sb.Append($"{rows.Length} installed package(s) found.\n");
        return sb.ToString();
    }
}
