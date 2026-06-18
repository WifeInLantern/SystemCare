namespace SystemCare.Models;

public class WindowsUpdateItem
{
    public string Title { get; init; } = "";
    public string Kb { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool IsMandatory { get; init; }
    public string Category { get; init; } = "";
    /// <summary>Index into the service's cached COM update collection (used for install).</summary>
    public int Index { get; init; }
}

public class WindowsUpdateProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string Title { get; init; } = "";
    public double Percent { get; init; }
}

public class WindowsUpdateInstallResult
{
    public int Installed { get; init; }
    public int Failed { get; init; }
    public bool RebootRequired { get; init; }
    public string Message { get; init; } = "";
}

public class WindowsUpdateHistoryItem
{
    public string Title { get; init; } = "";
    public DateTime Date { get; init; }
    public string Result { get; init; } = "";
    public string DateText => Date == default ? "" : Date.ToLocalTime().ToString("g");
}
