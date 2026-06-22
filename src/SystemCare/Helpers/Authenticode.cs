using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SystemCare.Helpers;

/// <summary>Outcome of an Authenticode check on a file.</summary>
public enum SignatureStatus
{
    /// <summary>No Authenticode signature is present.</summary>
    Unsigned,
    /// <summary>A signature is present but invalid — tampered, expired, distrusted, or chain-broken.</summary>
    Untrusted,
    /// <summary>A valid signature chaining to a trusted root.</summary>
    Trusted,
}

/// <summary>An Authenticode result plus the signer's identity (for publisher pinning).</summary>
public sealed record SignatureInfo(SignatureStatus Status, string? Subject, string? Thumbprint);

/// <summary>
/// Authenticode verification for a downloaded installer, using the same WinVerifyTrust check Windows
/// itself performs (signature present + certificate chain trusted), plus the signer's subject/thumbprint
/// for publisher pinning. This is OS-dependent P/Invoke; the allow/reject <em>decision</em> lives in the
/// pure <see cref="Services.UpdateSignaturePolicy"/> so it can be unit-tested without a signed fixture.
/// </summary>
public static class Authenticode
{
    public static SignatureInfo Verify(string filePath)
    {
        SignatureStatus status = VerifyTrust(filePath);

        string? subject = null, thumbprint = null;
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            subject = cert.Subject;
            thumbprint = cert.Thumbprint;
        }
        catch (Exception)
        {
            // Unsigned or the signer certificate couldn't be read.
        }

        return new SignatureInfo(status, subject, thumbprint);
    }

    private static SignatureStatus VerifyTrust(string filePath)
    {
        IntPtr pFileInfo = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };
            pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, pFileInfo, fDeleteOld: false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFileInfo,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_SAFER_FLAG,
            };

            try
            {
                int result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data);
                return result switch
                {
                    0 => SignatureStatus.Trusted,
                    unchecked((int)0x800B0100) => SignatureStatus.Unsigned, // TRUST_E_NOSIGNATURE
                    _ => SignatureStatus.Untrusted,                          // tampered/distrusted/expired/etc.
                };
            }
            finally
            {
                // Release the WinVerifyTrust state regardless of the verify outcome.
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data);
            }
        }
        catch (Exception)
        {
            // wintrust unavailable or a marshalling failure — treat as unverifiable (not trusted).
            return SignatureStatus.Untrusted;
        }
        finally
        {
            if (pFileInfo != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(pFileInfo);
                Marshal.FreeHGlobal(pFileInfo);
            }
        }
    }

    // ----- WinVerifyTrust interop -----

    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x100;

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        ref WinTrustData pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
