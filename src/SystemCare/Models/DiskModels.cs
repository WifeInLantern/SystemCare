namespace SystemCare.Models;

public class FileSystemNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long Size { get; set; }
    public bool IsDirectory { get; init; }
    public FileSystemNode? Parent { get; set; }
    public List<FileSystemNode> Children { get; } = [];
}

public class LargeFileEntry
{
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string Name => Path.GetFileName(FullPath);
    public string Directory => Path.GetDirectoryName(FullPath) ?? "";
}

public class DiskScanProgress
{
    public string CurrentPath { get; init; } = "";
    public long BytesSeen { get; init; }
    public int FilesSeen { get; init; }
}

public class DiskScanResult
{
    public required FileSystemNode Root { get; init; }
    public required List<LargeFileEntry> LargeFiles { get; init; }
    public int FilesSeen { get; init; }
    public int InaccessibleEntries { get; init; }
}
