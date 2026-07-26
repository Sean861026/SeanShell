namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerMetadata(
    string PluginId,
    string AssemblySha256,
    string PublisherCertificateSha256,
    int GrantedCapabilities);
