namespace SeanShell.Plugins;

public enum ExternalPluginCandidateStatus
{
    InvalidManifest,
    UnsafePath,
    MissingAssembly,
    Unsigned,
    RevokedSignature,
    RevocationUnavailable,
    ExpiredSignature,
    ExplicitlyDistrusted,
    UntrustedSignature,
    PublisherMismatch,
    ReadyForConsent,
}
