namespace SystemCare.Helpers;

/// <summary>
/// Central place for filesystem-walking rules shared by every scanner:
/// never follow reparse points (junctions/symlinks), never skip hidden/system
/// junk, and never let a single inaccessible entry abort a walk.
/// </summary>
public static class SafeFileEnumerator
{
    /// <summary>
    /// The default EnumerationOptions.AttributesToSkip is Hidden|System which would
    /// miss hidden junk files; we override it to skip only reparse points so that
    /// junctions and symlinks are never followed.
    /// </summary>
    public static EnumerationOptions RecursiveOptions() => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static EnumerationOptions TopLevelOptions() => new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Enumerates all files under <paramref name="root"/> recursively; returns empty on any failure.</summary>
    public static IEnumerable<FileInfo> EnumerateFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;

        IEnumerator<FileInfo>? enumerator = null;
        try
        {
            enumerator = new DirectoryInfo(root).EnumerateFiles("*", RecursiveOptions()).GetEnumerator();
        }
        catch (Exception)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext()) break;
                }
                catch (Exception)
                {
                    break;
                }
                yield return enumerator.Current;
            }
        }
    }

    /// <summary>Sums file sizes under <paramref name="root"/>; inaccessible entries count as zero.</summary>
    public static (long Bytes, int Files) Measure(string root)
    {
        long bytes = 0;
        int files = 0;
        foreach (var file in EnumerateFiles(root))
        {
            try
            {
                bytes += file.Length;
                files++;
            }
            catch (Exception)
            {
                // length unavailable — skip
            }
        }
        return (bytes, files);
    }
}
