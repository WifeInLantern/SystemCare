namespace SystemCare.Models;

public class ProcessEntry
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public string Title { get; init; } = "";
    public long WorkingSetBytes { get; init; }
    public double CpuPercent { get; init; }
}
