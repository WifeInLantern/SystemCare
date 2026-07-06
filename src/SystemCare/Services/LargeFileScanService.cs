using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface ILargeFileScanService
{
    /// <summary>
    /// Scans <paramref name="root"/> recursively for files at or above <paramref name="minBytes"/>,
    /// returning the largest ones (capped) sorted by size. Reparse points are never followed.
    /// </summary>
    Task<List<LargeFileInfo>> ScanAsync(string root, long minBytes, CancellationToken ct);
}

public class LargeFileScanService : ILargeFileScanService
{
    private const int MaxResults = 500;
    private readonly ILogService _log;

    public LargeFileScanService(ILogService log) => _log = log;

    public Task<List<LargeFileInfo>> ScanAsync(string root, long minBytes, CancellationToken ct) => Task.Run(() =>
    {
        var found = new List<LargeFileInfo>();
        try
        {
            foreach (var fi in SafeFileEnumerator.EnumerateFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                long size;
                DateTime access;
                try
                {
                    size = fi.Length;
                    if (size < minBytes) continue;
                    access = fi.LastAccessTimeUtc;
                }
                catch (Exception) { continue; } // file vanished / access denied mid-walk

                found.Add(new LargeFileInfo(fi.FullName, fi.Name, fi.DirectoryName ?? "", size, access));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.Warn("LargeFiles", $"Scan error: {ex.Message}"); }

        return found
            .OrderByDescending(f => f.SizeBytes)
            .Take(MaxResults)
            .ToList();
    }, ct);
}
