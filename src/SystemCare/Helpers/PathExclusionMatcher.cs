namespace SystemCare.Helpers;

/// <summary>
/// Decides whether a filesystem path falls inside a user-configured exclusion (e.g.
/// <c>AppSettings.CleanupExclusions</c>). Pure string matching, split out of the scanner that uses it
/// so the matching rules can be unit tested without touching the filesystem or settings store.
/// </summary>
public static class PathExclusionMatcher
{
    /// <summary>True when <paramref name="fullPath"/> equals an exclusion or sits inside one.</summary>
    public static bool IsExcluded(string fullPath, IReadOnlyCollection<string> exclusions)
    {
        if (exclusions.Count == 0) return false;
        foreach (var ex in exclusions)
        {
            if (string.IsNullOrWhiteSpace(ex)) continue;
            string normalized = ex.TrimEnd('\\');
            if (fullPath.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase) ||
                fullPath.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
