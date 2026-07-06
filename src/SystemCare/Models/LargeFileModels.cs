namespace SystemCare.Models;

/// <summary>One large file found by the Large &amp; Old Files scanner.</summary>
public record LargeFileInfo(string Path, string Name, string Directory, long SizeBytes, DateTime LastAccessUtc);
