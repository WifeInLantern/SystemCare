namespace SystemCare.Models;

public class RestorePoint
{
    public uint SequenceNumber { get; init; }
    public string Description { get; init; } = "";
    public DateTime CreationTime { get; init; }
    public string TypeText { get; init; } = "";
}
