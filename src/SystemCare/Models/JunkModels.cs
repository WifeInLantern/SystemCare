namespace SystemCare.Models;

/// <summary>A whitelisted junk location. The cleaner can only ever touch paths produced by one of these.</summary>
public class JunkCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool EnabledByDefault { get; init; } = true;
    /// <summary>True for the recycle bin, which is sized/emptied via shell APIs, never enumerated.</summary>
    public bool IsRecycleBin { get; init; }
    /// <summary>Roots to scan recursively. Resolved at scan time; missing directories are skipped.</summary>
    public Func<IEnumerable<string>> GetRoots { get; init; } = () => [];
    /// <summary>When true, only files older than the configured age are eligible (protects in-use temp files).</summary>
    public bool ApplyAgeFilter { get; init; }
}

public class JunkItem
{
    public required string Path { get; init; }
    public long Bytes { get; init; }
}

public class JunkCategoryResult
{
    public required JunkCategory Category { get; init; }
    public List<JunkItem> Items { get; } = [];
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
}

public class JunkScanResult
{
    public List<JunkCategoryResult> Categories { get; } = [];
    public long TotalBytes => Categories.Sum(c => c.TotalBytes);
    public int TotalFiles => Categories.Sum(c => c.FileCount);
    public DateTime CompletedUtc { get; init; } = DateTime.UtcNow;
}

public class CleanResult
{
    public long BytesRemoved { get; set; }
    public int FilesRemoved { get; set; }
    public int FilesSkipped { get; set; }
}

public class ScanProgress
{
    public string CurrentPath { get; init; } = "";
    public long BytesFound { get; init; }
    public int FilesFound { get; init; }
}
