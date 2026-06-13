namespace SystemCare.Models;

public class NetConnection
{
    public string Protocol { get; init; } = "TCP";
    public required string Local { get; init; }
    public required string Remote { get; init; }
    public string State { get; init; } = "";
    public int Pid { get; init; }
    public string ProcessName { get; init; } = "";
}
