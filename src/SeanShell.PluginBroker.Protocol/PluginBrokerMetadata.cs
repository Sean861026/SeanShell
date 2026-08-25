namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerMetadata(
    string PluginId,
    string AssemblySha256,
    string PublisherCertificateSha256,
    int GrantedCapabilities,
    int DependencyCount = 0,
    string? DependencySetSha256 = null,
    string? EntryType = null);
