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
    /// <summary>
    /// True for a drive root, a system or profile folder, or any folder that contains one. Shredding is
    /// irreversible and runs elevated, so these are refused outright rather than left to one confirmation.
    /// </summary>
    public static bool IsProtectedPath(string path)
    {
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); }
        catch (Exception) { return true; }
        if (full.Length <= 2) return true; // a drive root ("C:" once trimmed)

        static string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f).TrimEnd('\\');
        string profile = Folder(Environment.SpecialFolder.UserProfile);

        // OS and application folders: refused together with everything inside them.
        string[] system =
        [
            Folder(Environment.SpecialFolder.Windows),
            Folder(Environment.SpecialFolder.ProgramFiles),
            Folder(Environment.SpecialFolder.ProgramFilesX86),
            Folder(Environment.SpecialFolder.CommonApplicationData),
        ];
        // Containers of user data: the folder itself is refused, but files inside it can still be shredded.
        string[] containers =
        [
            profile,
            Path.GetDirectoryName(profile) ?? "",
            Folder(Environment.SpecialFolder.ApplicationData),
            Folder(Environment.SpecialFolder.LocalApplicationData),
        ];

        bool IsSelfOrAncestorOf(string root) =>
            full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase);

        foreach (var root in system.Where(r => r.Length > 0))
            if (IsSelfOrAncestorOf(root) || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        foreach (var root in containers.Where(r => r.Length > 0))
            if (IsSelfOrAncestorOf(root))
                return true;
        return false;
    }

    public Task<ShredResult> ShredAsync(IEnumerable<string> paths, int passes, IProgress<ShredProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        // Defence in depth: the page refuses these too, but the service never shreds them whatever the caller.
        paths = paths.Where(p => !IsProtectedPath(p)).ToList();

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

        // Remove the folders that were passed in, now that their files are gone. Subfolders go bottom-up and only
        // when empty, so a skipped (locked) file keeps its folder chain, and the shredded folder never lingers
        // as an empty tree. Only real directories are walked; a junction is removed as a link, never followed.
        foreach (var path in paths.Where(Directory.Exists).OrderByDescending(p => p.Length))
        {
            try
            {
                var subdirs = Directory.GetDirectories(path, "*", SafeFileEnumerator.RecursiveOptions())
                    .OrderByDescending(d => d.Length);
                foreach (var dir in subdirs.Append(path))
                {
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        return result;
    }, ct);
}
