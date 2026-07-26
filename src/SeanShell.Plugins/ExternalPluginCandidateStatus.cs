namespace SeanShell.Plugins;

public enum ExternalPluginCandidateStatus
{
    InvalidManifest,
    UnsafePath,
    MissingAssembly,
    Unsigned,
    UntrustedSignature,
    PublisherMismatch,
    ReadyForConsent,
}
