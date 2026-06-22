using SystemCare.Helpers;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// The pure allow/reject decision for an installer's Authenticode result (C2). Verifies that tampered/
/// untrusted signatures are always refused, unsigned is gated by <c>requireSigned</c>, and a trusted
/// signature can be pinned to an expected publisher.
/// </summary>
public class UpdateSignaturePolicyTests
{
    private static SignatureInfo Sig(SignatureStatus status, string? subject = "CN=SystemCare, O=SystemCare") =>
        new(status, subject, "THUMBPRINT");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Untrusted_IsAlwaysRejected(bool requireSigned)
    {
        var d = UpdateSignaturePolicy.Evaluate(Sig(SignatureStatus.Untrusted), requireSigned, expectedPublisher: null);

        Assert.False(d.Allowed);
        Assert.Contains("not trusted", d.Reason);
    }

    [Fact]
    public void Unsigned_AllowedWhenNotRequired()
    {
        var d = UpdateSignaturePolicy.Evaluate(Sig(SignatureStatus.Unsigned, null), requireSigned: false, null);

        Assert.True(d.Allowed);
        Assert.Contains("unsigned", d.Reason);
    }

    [Fact]
    public void Unsigned_RejectedWhenSignedRequired()
    {
        var d = UpdateSignaturePolicy.Evaluate(Sig(SignatureStatus.Unsigned, null), requireSigned: true, null);

        Assert.False(d.Allowed);
        Assert.Contains("not signed", d.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Trusted_NoPublisherPinning_IsAllowed(string? expectedPublisher)
    {
        var d = UpdateSignaturePolicy.Evaluate(Sig(SignatureStatus.Trusted), requireSigned: true, expectedPublisher);

        Assert.True(d.Allowed);
        Assert.Contains("verified", d.Reason);
    }

    [Theory]
    [InlineData("SystemCare")]
    [InlineData("systemcare")]   // case-insensitive substring of the subject
    public void Trusted_PublisherMatch_IsAllowed(string expectedPublisher)
    {
        var d = UpdateSignaturePolicy.Evaluate(Sig(SignatureStatus.Trusted), requireSigned: true, expectedPublisher);

        Assert.True(d.Allowed);
    }

    [Fact]
    public void Trusted_PublisherMismatch_IsRejected()
    {
        var d = UpdateSignaturePolicy.Evaluate(
            Sig(SignatureStatus.Trusted, "CN=Someone Else, O=Evil"), requireSigned: true, "SystemCare");

        Assert.False(d.Allowed);
        Assert.Contains("publisher mismatch", d.Reason);
    }

    [Fact]
    public void Trusted_PinningWithUnknownSubject_IsRejected()
    {
        var d = UpdateSignaturePolicy.Evaluate(Sig(SignatureStatus.Trusted, null), requireSigned: true, "SystemCare");

        Assert.False(d.Allowed);
    }
}
