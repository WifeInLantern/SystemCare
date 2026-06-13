using System.Security.Cryptography;
using SystemCare.Helpers;

namespace SystemCare.Services;

public class ShredProgress
{
    public string CurrentFile { get; init; } = "";
    public int FilesDone { get; init; }
    public int FilesTotal { get; init; }
}

public class ShredResult
{
    public int FilesShredded { get; set; }
    public int FilesSkipped { get; set; }
    public long BytesShredded { get; set; }
}

public interface IFileShredderService
{
    /// <summary>Overwrites each file's contents <paramref name="passes"/> times, then deletes it. Irreversible.</summary>
    Task<ShredResult> ShredAsync(IEnumerable<string> paths, int passes, IProgress<ShredProgress>? progress, CancellationToken ct);
}

public class FileShredderService : IFileShredderService
{
    public Task<ShredResult> ShredAsync(IEnumerable<string> paths, int passes, IProgress<ShredProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        // Expand folders into their files (reparse-point-safe).
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(SafeFileEnumerator.EnumerateFiles(path).Select(f => f.FullName));
            else if (File.Exists(path))
                files.Add(path);
        }
        files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var result = new ShredResult();
        int total = files.Count;
        int done = 0;
        passes = Math.Clamp(passes, 1, 7);
        var buffer = new byte[81920];

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress?.Report(new ShredProgress { CurrentFile = file, FilesDone = done, FilesTotal = total });

            try
            {
                var info = new FileInfo(file);
                long length = info.Length;
                if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                    info.Attributes &= ~FileAttributes.ReadOnly;

                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    for (int pass = 0; pass < passes; pass++)
                    {
                        ct.ThrowIfCancellationRequested();
                        fs.Seek(0, SeekOrigin.Begin);
                        long remaining = length;
                        while (remaining > 0)
                        {
                            RandomNumberGenerator.Fill(buffer);
                            int chunk = (int)Math.Min(buffer.Length, remaining);
                            fs.Write(buffer, 0, chunk);
                            remaining -= chunk;
                        }
                        fs.Flush(flushToDisk: true);
                    }
                    fs.SetLength(0);
                }

                File.Delete(file);
                result.FilesShredded++;
                result.BytesShredded += length;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                result.FilesSkipped++; // in use / access denied — leave it
            }
        }

        // Remove now-empty folders that were passed in.
        foreach (var path in paths.Where(Directory.Exists).OrderByDescending(p => p.Length))
        {
            try { if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, recursive: true); }
            catch (Exception) { }
        }

        return result;
    }, ct);
}
