namespace SystemCare.Models;

public class DuplicateFile
{
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string Name => Path.GetFileName(FullPath);
}

public class DuplicateGroup
{
    public long Size { get; init; }
    public required List<DuplicateFile> Files { get; init; }
    public long WastedBytes => Size * (Files.Count - 1);
}

public enum DuplicateStage { Enumerating, PartialHashing, FullHashing }

public class DuplicateScanProgress
{
    public DuplicateStage Stage { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public string CurrentFile { get; init; } = "";
}
