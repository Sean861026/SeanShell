namespace SeanShell.Plugins;

public sealed record AuthenticodeVerificationResult(
    bool IsTrusted,
    string Detail,
    string? SignerCertificateSha256 = null);
