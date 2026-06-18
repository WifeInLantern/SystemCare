namespace SystemCare.Helpers;

public static class TextHelpers
{
    /// <summary>
    /// Joins the non-empty parts with " · ". A missing/blank field is dropped, so a detail line can
    /// never start or end with an orphan separator (the source of several cross-device formatting bugs).
    /// </summary>
    public static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
