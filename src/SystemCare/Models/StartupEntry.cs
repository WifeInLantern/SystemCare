namespace SystemCare.Models;

public enum StartupSource
{
    HkcuRun,
    HklmRun,
    HklmRun32,
    UserStartupFolder,
    CommonStartupFolder,
    ScheduledTask,
}

public class StartupEntry
{
    public required string Name { get; init; }
    public required StartupSource Source { get; init; }
    public string Command { get; init; } = "";
    public string? ResolvedExePath { get; init; }
    public string Publisher { get; init; } = "";
    public bool IsEnabled { get; set; }
    /// <summary>Registry value name, .lnk file path, or scheduled-task path — whatever identifies the entry in its store.</summary>
    public required string RawKey { get; init; }

    public string SourceDisplay => Source switch
    {
        StartupSource.HkcuRun => "Registry (user)",
        StartupSource.HklmRun => "Registry (machine)",
        StartupSource.HklmRun32 => "Registry (machine, 32-bit)",
        StartupSource.UserStartupFolder => "Startup folder (user)",
        StartupSource.CommonStartupFolder => "Startup folder (common)",
        StartupSource.ScheduledTask => "Scheduled task",
        _ => Source.ToString(),
    };
}
