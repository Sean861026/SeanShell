using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed record ExternalPluginConsent(
    string PluginId,
    string PublisherCertificateSha256,
    PluginCapability GrantedCapabilities,
    DateTimeOffset GrantedAtUtc,
    string? EntryType = null);
