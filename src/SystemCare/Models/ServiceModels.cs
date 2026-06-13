namespace SystemCare.Models;

public enum ServiceState { Running, Stopped, Paused, Pending, Unknown }

public class ServiceEntry
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public ServiceState State { get; set; }
    public string StartMode { get; init; } = "";
    public string Description { get; init; } = "";
    public bool CanStop { get; set; }
}
