using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed record ExternalPluginCandidate(
    string PackageDirectoryName,
    string? Id,
    string Name,
    string? Version,
    string? Publisher,
    string? EntryAssembly,
    PluginCapability Capabilities,
    ExternalPluginCandidateStatus Status,
    string Detail,
    string? AssemblySha256 = null,
    string? SignerCertificateSha256 = null);
