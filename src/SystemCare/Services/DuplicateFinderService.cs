using System.IO.Hashing;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IDuplicateFinderService
{
    Task<List<DuplicateGroup>> FindAsync(IEnumerable<string> roots, long minSizeBytes,
        IProgress<DuplicateScanProgress>? progress, CancellationToken ct);
}

public class DuplicateFinderService : IDuplicateFinderService
{
    private const int PartialHashBytes = 64 * 1024;

    public Task<List<DuplicateGroup>> FindAsync(IEnumerable<string> roots, long minSizeBytes,
        IProgress<DuplicateScanProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        // Stage 1: enumerate and group by exact length.
        var bySize = new Dictionary<long, List<FileInfo>>();
        int seen = 0;
        foreach (var root in roots)
        {
            foreach (var file in SafeFileEnumerator.EnumerateFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (file.Length < minSizeBytes) continue;
                    if (!bySize.TryGetValue(file.Length, out var list))
                        bySize[file.Length] = list = [];
                    list.Add(file);
                    if (++seen % 200 == 0)
                        progress?.Report(new DuplicateScanProgress
                        {
                            Stage = DuplicateStage.Enumerating,
                            Current = seen,
                            CurrentFile = file.FullName,
                        });
                }
                catch (Exception) { }
            }
        }

        var sizeCandidates = bySize.Values.Where(l => l.Count > 1).ToList();

        // Stage 2: partial hash (first 64 KB) within each same-size group.
        var partialGroups = new List<List<FileInfo>>();
        int partialTotal = sizeCandidates.Sum(g => g.Count);
        int partialDone = 0;
        foreach (var group in sizeCandidates)
        {
            var byPartial = new Dictionary<ulong, List<FileInfo>>();
            foreach (var file in group)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new DuplicateScanProgress
                {
                    Stage = DuplicateStage.PartialHashing,
                    Current = ++partialDone,
                    Total = partialTotal,
                    CurrentFile = file.FullName,
                });
                try
                {
                    ulong hash = PartialHash(file.FullName);
                    if (!byPartial.TryGetValue(hash, out var list))
                        byPartial[hash] = list = [];
                    list.Add(file);
                }
                catch (Exception) { }
            }
            partialGroups.AddRange(byPartial.Values.Where(l => l.Count > 1));
        }

        // Stage 3: full streaming hash to confirm.
        var groups = new List<DuplicateGroup>();
        int fullTotal = partialGroups.Sum(g => g.Count);
        int fullDone = 0;
        foreach (var group in partialGroups)
        {
            var byFull = new Dictionary<string, List<FileInfo>>();
            foreach (var file in group)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new DuplicateScanProgress
                {
                    Stage = DuplicateStage.FullHashing,
                    Current = ++fullDone,
                    Total = fullTotal,
                    CurrentFile = file.FullName,
                });
                try
                {
                    string hash = FullHash(file.FullName, ct);
                    if (!byFull.TryGetValue(hash, out var list))
                        byFull[hash] = list = [];
                    list.Add(file);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { }
            }

            foreach (var confirmed in byFull.Values.Where(l => l.Count > 1))
            {
                groups.Add(new DuplicateGroup
                {
                    Size = confirmed[0].Length,
                    Files = confirmed.Select(f => new DuplicateFile
                    {
                        FullPath = f.FullName,
                        Size = f.Length,
                        ModifiedUtc = f.LastWriteTimeUtc,
                    }).ToList(),
                });
            }
        }

        return groups.OrderByDescending(g => g.WastedBytes).ToList();
    }, ct);

    private static ulong PartialHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, PartialHashBytes);
        byte[] buffer = new byte[PartialHashBytes];
        int read = stream.Read(buffer, 0, buffer.Length);
        return XxHash3.HashToUInt64(buffer.AsSpan(0, read));
    }

    private static string FullHash(string path, CancellationToken ct)
    {
        var hasher = new XxHash128();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
        byte[] buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hasher.Append(buffer.AsSpan(0, read));
        }
        return Convert.ToHexString(hasher.GetCurrentHash());
    }
}
