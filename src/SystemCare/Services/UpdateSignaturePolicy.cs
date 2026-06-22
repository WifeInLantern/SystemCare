using SystemCare.Helpers;

namespace SystemCare.Services;

/// <summary>
/// Pure allow/reject decision for a downloaded installer's Authenticode result, kept separate from the
/// OS verification (<see cref="Authenticode"/>) so it is unit-testable.
///
/// Policy:
/// <list type="bullet">
/// <item>A present-but-<b>untrusted</b> signature (tampered, distrusted, broken chain) is always rejected.</item>
/// <item>An <b>unsigned</b> installer is allowed unless <paramref name="requireSigned"/> is set — today's
/// releases are unsigned, and integrity is already covered by the SHA-256 checksum.</item>
/// <item>A <b>trusted</b> signature is accepted, and when an <c>expectedPublisher</c> is configured the
/// signer's subject must contain it (publisher pinning), so a different validly-signed binary is rejected.</item>
/// </list>
/// </summary>
public static class UpdateSignaturePolicy
{
    public readonly record struct Decision(bool Allowed, string Reason);

    /// <param name="sig">The Authenticode result for the downloaded file.</param>
    /// <param name="requireSigned">When true, an unsigned installer is rejected.</param>
    /// <param name="expectedPublisher">Optional subject substring the signer must contain (publisher pinning).</param>
    public static Decision Evaluate(SignatureInfo sig, bool requireSigned, string? expectedPublisher)
    {
        if (sig.Status == SignatureStatus.Untrusted)
            return new Decision(false, $"installer signature is not trusted ({Describe(sig.Subject)})");

        if (sig.Status == SignatureStatus.Unsigned)
            return requireSigned
                ? new Decision(false, "installer is not signed and signed updates are required")
                : new Decision(true, "installer is unsigned (allowed; integrity verified by checksum)");

        // Trusted — optionally pin the publisher.
        if (!string.IsNullOrWhiteSpace(expectedPublisher) &&
            (sig.Subject is null ||
             sig.Subject.IndexOf(expectedPublisher, StringComparison.OrdinalIgnoreCase) < 0))
            return new Decision(false, $"installer publisher mismatch ({Describe(sig.Subject)})");

        return new Decision(true, $"installer signature verified ({Describe(sig.Subject)})");
    }

    private static string Describe(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? "unknown signer" : subject!;
}
