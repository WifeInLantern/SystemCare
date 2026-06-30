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
}
