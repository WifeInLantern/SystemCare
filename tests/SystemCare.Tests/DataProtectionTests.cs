using SystemCare.Helpers;
using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// DPAPI round-trip for the at-rest GitHub token (C1). Runs as the current user, so CurrentUser-scoped
/// ProtectedData works in the test process. Verifies the secret is never stored or returned in clear.
/// </summary>
public class DataProtectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Protect_EmptyInput_ReturnsNull(string? input)
    {
        Assert.Null(DataProtection.Protect(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Unprotect_EmptyInput_ReturnsEmpty(string? input)
    {
        Assert.Equal("", DataProtection.Unprotect(input));
    }

    [Fact]
    public void ProtectThenUnprotect_RoundTripsTheSecret()
    {
        const string secret = "github_pat_11ABCDEFG_secretvalue";

        string? cipher = DataProtection.Protect(secret);

        Assert.NotNull(cipher);
        Assert.DoesNotContain(secret, cipher);                 // ciphertext must not leak the plaintext
        Assert.Equal(secret, DataProtection.Unprotect(cipher));
    }

    [Fact]
    public void Unprotect_GarbageOrForeignBlob_ReturnsEmptyNotThrow()
    {
        Assert.Equal("", DataProtection.Unprotect("not-valid-base64!!"));
        Assert.Equal("", DataProtection.Unprotect("aGVsbG8gd29ybGQ="));  // valid base64, not a DPAPI blob
    }
}

/// <summary>The <see cref="AppSettings.UpdateGitHubToken"/> facade encrypts into the persisted field.</summary>
public class AppSettingsTokenTests
{
    [Fact]
    public void Default_HasNoToken()
    {
        var s = new AppSettings();
        Assert.Equal("", s.UpdateGitHubToken);
        Assert.Null(s.GitHubTokenProtected);
    }

    [Fact]
    public void SettingToken_StoresOnlyTheEncryptedForm()
    {
        const string token = "github_pat_token_value";
        var s = new AppSettings { UpdateGitHubToken = token };

        Assert.NotNull(s.GitHubTokenProtected);                 // persisted field is the encrypted blob
        Assert.DoesNotContain(token, s.GitHubTokenProtected!);  // never the plaintext
        Assert.Equal(token, s.UpdateGitHubToken);               // facade decrypts back
    }

    [Fact]
    public void ClearingToken_RemovesThePersistedSecret()
    {
        var s = new AppSettings { UpdateGitHubToken = "something" };

        s.UpdateGitHubToken = "";

        Assert.Null(s.GitHubTokenProtected);
        Assert.Equal("", s.UpdateGitHubToken);
    }
}
