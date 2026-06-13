using SystemCare.Helpers;

namespace SystemCare.Services;

public interface IEmptyFolderService
{
    /// <summary>Finds the top-most recursively-empty folders under <paramref name="root"/>
    /// (folders that contain no files anywhere beneath them).</summary>
    Task<List<string>> ScanAsync(string root, IProgress<string>? progress, CancellationToken ct);
}

public class EmptyFolderService : IEmptyFolderService
{
    public Task<List<string>> ScanAsync(string root, IProgress<string>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var results = new List<string>();
        var lastReport = DateTime.MinValue;

        // Returns true if 'dir' is recursively empty (no files anywhere below). Reports a child as
        // "top-most empty" only when its parent is NOT empty, so the user sees the meaningful set.
        bool Walk(string dir)
        {
            ct.ThrowIfCancellationRequested();

            if (progress is not null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 80)
            {
                lastReport = DateTime.UtcNow;
                progress.Report(dir);
            }

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir, "*", SafeFileEnumerator.TopLevelOptions());
            }
            catch (Exception)
            {
                return false; // inaccessible — treat as non-empty so we never touch it
            }

            bool hasFiles;
            try
            {
                hasFiles = Directory.EnumerateFiles(dir, "*", SafeFileEnumerator.TopLevelOptions()).Any();
            }
            catch (Exception)
            {
                return false;
            }

            var childEmpty = new bool[subdirs.Length];
            bool allChildrenEmpty = true;
            for (int i = 0; i < subdirs.Length; i++)
            {
                childEmpty[i] = Walk(subdirs[i]);
                if (!childEmpty[i]) allChildrenEmpty = false;
            }

            bool empty = !hasFiles && allChildrenEmpty;
            if (!empty)
            {
                // This folder stays; surface its empty children as removable top-level items.
                for (int i = 0; i < subdirs.Length; i++)
                    if (childEmpty[i]) results.Add(subdirs[i]);
            }
            return empty;
        }

        try
        {
            if (Directory.Exists(root) && Walk(root))
                results.Add(root); // the whole root is empty
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return results;
    }, ct);
}
