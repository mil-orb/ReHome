using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Rehome.Scan;

/// <summary>
/// Reads who signed a file and whether Windows trusts that signature.
/// The signer comes from WinVerifyTrust's own result (the leaf it actually checked), never from
/// the first certificate in the file, which can be an intermediate or a root.
/// Read is offline on purpose: no revocation fetch, so a scan never waits on the network.
/// Restore also calls Distrusted on the installer it downloads, which checks revocation over the network.
/// </summary>
internal static unsafe partial class Authenticode
{
    public static Signer? Read(string? path, string from)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        fixed (char* p = path)
        {
            var file = new WintrustFileInfo { cbStruct = (uint)sizeof(WintrustFileInfo), pcwszFilePath = p };
            var data = new WintrustData
            {
                cbStruct = (uint)sizeof(WintrustData),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = &file,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE,
            };

            var action = GenericVerifyV2;
            var result = WinVerifyTrust(-1, &action, &data);
            try
            {
                var subject = SignerSubject(data.hWVTStateData);
                return subject is null ? null : Signer.Parse(subject, from, result == 0);
            }
            finally
            {
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                _ = WinVerifyTrust(-1, &action, &data);
            }
        }
    }

    /// <summary>
    /// True when Windows says this file's signature must not be trusted: its certificate was revoked, an admin
    /// distrusted it, or it chains to a root Windows doesn't trust. Checks revocation for the whole chain, online.
    /// A revocation server that can't be reached is not a verdict, so it returns false and Read's result stands.
    /// </summary>
    public static bool Distrusted(string path)
    {
        if (!File.Exists(path))
            return false;
        fixed (char* p = path)
        {
            var file = new WintrustFileInfo { cbStruct = (uint)sizeof(WintrustFileInfo), pcwszFilePath = p };
            var data = new WintrustData
            {
                cbStruct = (uint)sizeof(WintrustData),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = &file,
                dwStateAction = WTD_STATEACTION_VERIFY,
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(-1, &action, &data);
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            _ = WinVerifyTrust(-1, &action, &data);
            return IsDistrust(result);
        }
    }

    internal static bool IsDistrust(int result) => unchecked((uint)result) is CERT_E_REVOKED or TRUST_E_EXPLICIT_DISTRUST or CERT_E_UNTRUSTEDROOT;

    const uint CERT_E_REVOKED = 0x800B010C;
    const uint TRUST_E_EXPLICIT_DISTRUST = 0x800B0111;
    const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
    const uint WTD_REVOKE_WHOLECHAIN = 1;

    static string? SignerSubject(nint state)
    {
        if (state == 0)
            return null;

        var provider = WTHelperProvDataFromStateData(state);
        if (provider == 0)
            return null;

        var signer = WTHelperGetProvSignerFromChain(provider, 0, 0, 0);
        if (signer is null || signer->csCertChain == 0 || signer->pasCertChain is null)
            return null;

        var leaf = signer->pasCertChain[0].pCert;
        if (leaf == 0)
            return null;

        // Duplicates the context, so it stays valid after WinVerifyTrust frees its own copy.
        using var cert = new X509Certificate(leaf);
        return cert.Subject;
    }

    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    const uint WTD_UI_NONE = 2;
    const uint WTD_REVOKE_NONE = 0;
    const uint WTD_CHOICE_FILE = 1;
    const uint WTD_STATEACTION_VERIFY = 1;
    const uint WTD_STATEACTION_CLOSE = 2;
    const uint WTD_REVOCATION_CHECK_NONE = 0x10;
    const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    struct WintrustFileInfo
    {
        public uint cbStruct;
        public char* pcwszFilePath;
        public nint hFile;
        public Guid* pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WintrustData
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public WintrustFileInfo* pFile;
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CryptProviderSigner
    {
        public uint cbStruct;
        public uint sftVerifyAsOfLow;   // FILETIME is two DWORDs: 4-byte aligned, not 8.
        public uint sftVerifyAsOfHigh;
        public uint csCertChain;
        public CryptProviderCert* pasCertChain;
        // Remaining fields unused.
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CryptProviderCert
    {
        public uint cbStruct;
        public nint pCert;
        // Remaining fields unused.
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint hwnd, Guid* action, WintrustData* data);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperProvDataFromStateData(nint state);

    [LibraryImport("wintrust.dll")]
    private static partial CryptProviderSigner* WTHelperGetProvSignerFromChain(nint provider, uint signerIndex, int counterSigner, uint counterSignerIndex);
}
