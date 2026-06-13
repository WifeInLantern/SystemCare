using SystemCare.Models;

namespace SystemCare.Services;

public interface IDiskAnalyzerService
{
    /// <summary>Builds a directory-size tree and collects the top-N largest files in a single pass.</summary>
    Task<DiskScanResult> ScanAsync(string root, int topN, long minLargeFileBytes,
        IProgress<DiskScanProgress>? progress, CancellationToken ct);
}

public class DiskAnalyzerService : IDiskAnalyzerService
{
    public Task<DiskScanResult> ScanAsync(string root, int topN, long minLargeFileBytes,
        IProgress<DiskScanProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var rootNode = new FileSystemNode
        {
            Name = root,
            FullPath = root,
            IsDirectory = true,
        };

        var state = new ScanState
        {
            Progress = progress,
            Ct = ct,
            TopN = topN,
            MinLargeFileBytes = minLargeFileBytes,
        };

        ScanDirectory(rootNode, state);

        var largeFiles = new List<LargeFileEntry>();
        while (state.LargeFiles.Count > 0)
            largeFiles.Add(state.LargeFiles.Dequeue());
        largeFiles.Reverse(); // dequeue order is smallest-first

        return new DiskScanResult
        {
            Root = rootNode,
            LargeFiles = largeFiles,
            FilesSeen = state.FilesSeen,
            InaccessibleEntries = state.Inaccessible,
        };
    }, ct);

    private sealed class ScanState
    {
        public required IProgress<DiskScanProgress>? Progress { get; init; }
        public required CancellationToken Ct { get; init; }
        public required int TopN { get; init; }
        public required long MinLargeFileBytes { get; init; }
        public PriorityQueue<LargeFileEntry, long> LargeFiles { get; } = new(); // min-heap by size
        public long BytesSeen;
        public int FilesSeen;
        public int Inaccessible;
        public DateTime LastReport = DateTime.MinValue;
    }

    private static void ScanDirectory(FileSystemNode node, ScanState state)
    {
        state.Ct.ThrowIfCancellationRequested();

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(node.FullPath).EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, // never follow junctions/symlinks
            });
        }
        catch (Exception)
        {
            state.Inaccessible++;
            return;
        }

        foreach (var entry in entries)
        {
            state.Ct.ThrowIfCancellationRequested();
            try
            {
                if (entry is DirectoryInfo dir)
                {
                    var child = new FileSystemNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Parent = node,
                    };
                    node.Children.Add(child);
                    ScanDirectory(child, state);
                    node.Size += child.Size;
                }
                else if (entry is FileInfo file)
                {
                    long size = file.Length;
                    node.Children.Add(new FileSystemNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = size,
                        Parent = node,
                    });
                    node.Size += size;
                    state.BytesSeen += size;
                    state.FilesSeen++;

                    if (size >= state.MinLargeFileBytes)
                    {
                        var large = new LargeFileEntry
                        {
                            FullPath = file.FullName,
                            Size = size,
                            ModifiedUtc = file.LastWriteTimeUtc,
                        };
                        if (state.LargeFiles.Count < state.TopN)
                        {
                            state.LargeFiles.Enqueue(large, size);
                        }
                        else if (state.LargeFiles.TryPeek(out _, out long smallest) && size > smallest)
                        {
                            state.LargeFiles.Dequeue();
                            state.LargeFiles.Enqueue(large, size);
                        }
                    }

                    if (state.Progress is not null && (DateTime.UtcNow - state.LastReport).TotalMilliseconds > 100)
                    {
                        state.LastReport = DateTime.UtcNow;
                        state.Progress.Report(new DiskScanProgress
                        {
                            CurrentPath = file.FullName,
                            BytesSeen = state.BytesSeen,
                            FilesSeen = state.FilesSeen,
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                state.Inaccessible++;
            }
        }

        // Largest first so the treemap and tree views read naturally.
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
    }
}
