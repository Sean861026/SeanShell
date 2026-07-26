using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SeanShell.Plugins;

namespace SeanShell.Windows;

public sealed class WindowsAuthenticodeVerifier : IAuthenticodeVerifier
{
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int CertEExpired = unchecked((int)0x800B0101);
    private const int CertERevoked = unchecked((int)0x800B010C);
    private const int CertERevocationFailure = unchecked((int)0x800B010E);
    private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
    private const int CryptENoRevocationDll = unchecked((int)0x80092011);
    private const int CryptENoRevocationCheck = unchecked((int)0x80092012);
    private const int CryptERevocationOffline = unchecked((int)0x80092013);
    private const uint RevocationCheckChainExcludeRoot = 0x00000080;

    private static readonly Guid GenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public AuthenticodeVerificationResult Verify(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var signerHash = TryGetSignerCertificateSha256(filePath);
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPointer = IntPtr.Zero;
        try
        {
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

            var trustData = new WinTrustData(fileInfoPointer);
            var result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref trustData);
            var verifiedAtUtc = DateTimeOffset.UtcNow;
            if (result == 0)
            {
                return new AuthenticodeVerificationResult(
                    AuthenticodeTrustStatus.Trusted,
                    "The Authenticode signature and publisher revocation status are trusted.",
                    signerHash,
                    verifiedAtUtc);
            }

            var error = new Win32Exception(result).Message;
            return new AuthenticodeVerificationResult(
                MapStatus(result, signerHash),
                BuildDetail(result, error, signerHash),
                signerHash,
                verifiedAtUtc);
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }
        }
    }

    private static AuthenticodeTrustStatus MapStatus(int result, string? signerHash) =>
        result switch
        {
            TrustENoSignature => AuthenticodeTrustStatus.Unsigned,
            CertERevoked => AuthenticodeTrustStatus.Revoked,
            CryptENoRevocationDll or
            CryptENoRevocationCheck or
            CryptERevocationOffline or
            CertERevocationFailure => AuthenticodeTrustStatus.RevocationUnavailable,
            CertEExpired => AuthenticodeTrustStatus.Expired,
            TrustEExplicitDistrust => AuthenticodeTrustStatus.ExplicitlyDistrusted,
            _ when signerHash is null => AuthenticodeTrustStatus.Unsigned,
            _ => AuthenticodeTrustStatus.Untrusted,
        };

    private static string BuildDetail(int result, string error, string? signerHash) =>
        MapStatus(result, signerHash) switch
        {
            AuthenticodeTrustStatus.Unsigned =>
                $"No verifiable Authenticode signer was found ({error}).",
            AuthenticodeTrustStatus.Revoked =>
                $"The Authenticode publisher certificate was revoked ({error}).",
            AuthenticodeTrustStatus.RevocationUnavailable =>
                $"Publisher revocation status could not be confirmed ({error}). Retry when certificate services are reachable.",
            AuthenticodeTrustStatus.Expired =>
                $"The Authenticode signing certificate is outside its valid lifetime ({error}).",
            AuthenticodeTrustStatus.ExplicitlyDistrusted =>
                $"Windows explicitly distrusts the Authenticode publisher ({error}).",
            _ => $"The Authenticode signature is not trusted ({error}).",
        };

    private static string? TryGetSignerCertificateSha256(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057 // Required to read the signer embedded in an Authenticode-signed PE file.
            using var certificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            return certificate.GetCertHashString(HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        public WinTrustFileInfo(string filePath)
        {
            StructureSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = filePath;
        }

        public uint StructureSize;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;

        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public WinTrustData(IntPtr fileInfo)
        {
            StructureSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2;
            RevocationChecks = 1;
            UnionChoice = 1;
            FileInfo = fileInfo;
            StateAction = 0;
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = RevocationCheckChainExcludeRoot;
            UiContext = 0;
            SignatureSettings = IntPtr.Zero;
        }

        public uint StructureSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
