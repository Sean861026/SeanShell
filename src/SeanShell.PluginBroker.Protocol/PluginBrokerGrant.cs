namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerGrant(
    string PluginId,
    string PackageDirectoryPath,
    string EntryAssemblyPath,
    string AssemblySha256,
    string PublisherCertificateSha256,
    int GrantedCapabilities,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    PluginBrokerDependency[]? Dependencies = null);
