namespace SeanShell.Plugins;

public enum AuthenticodeTrustStatus
{
    Trusted,
    Unsigned,
    Revoked,
    RevocationUnavailable,
    Expired,
    ExplicitlyDistrusted,
    Untrusted,
}

public sealed record AuthenticodeVerificationResult(
    AuthenticodeTrustStatus Status,
    string Detail,
    string? SignerCertificateSha256 = null,
    DateTimeOffset? VerifiedAtUtc = null)
{
    public bool IsTrusted => Status == AuthenticodeTrustStatus.Trusted;
}
