using System.Security.Cryptography;
using System.Text;

namespace SystemCare.Helpers;

/// <summary>
/// DPAPI (CurrentUser) protection for small secrets persisted to disk — currently the optional GitHub
/// token in settings.json. Ciphertext is base64 and bound to the current Windows user account, so a
/// copied/synced settings file (or another local user) can't read it. Encryption failures fail closed
/// (return null / empty) rather than ever round-tripping the secret in clear.
/// </summary>
public static class DataProtection
{
    // Extra entropy namespaces our blobs so they aren't interchangeable with other apps' DPAPI data.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SystemCare.Settings.v1");

    /// <summary>Encrypts a secret to a base64 DPAPI blob; returns null for empty input or on failure.</summary>
    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        try
        {
            byte[] cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Decrypts a base64 DPAPI blob; returns "" for empty input or on failure.</summary>
    public static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return "";
        try
        {
            byte[] plain = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            return "";
        }
    }
}
