namespace SystemCare.Models;

public enum SecurityStatus { Ok, Warning, Bad, Unknown }

public class SecurityCheck
{
    public required string Name { get; init; }
    public string Message { get; init; } = "";
    public SecurityStatus Status { get; init; } = SecurityStatus.Unknown;
    public string Icon { get; init; } = "ShieldCheckmark24";
    public string? FixLabel { get; init; }
    public string? FixTarget { get; init; } // shell-executed URI/command for the Fix button

    public string StatusText => Status switch
    {
        SecurityStatus.Ok => "OK",
        SecurityStatus.Warning => "Review",
        SecurityStatus.Bad => "At risk",
        _ => "Unknown",
    };
}
