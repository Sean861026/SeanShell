using SeanShell.PluginBroker.Protocol;

namespace SeanShell.Plugins;

public sealed class ExternalPluginBrokerProbeService
{
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromSeconds(15);
    private readonly ExternalPluginCatalog _catalog;
    private readonly PluginBrokerQuarantineManager _quarantine;
    private readonly ExternalPluginTrustManager _trust;
    private readonly IPluginBrokerProbeClient _broker;

    public ExternalPluginBrokerProbeService(
        ExternalPluginCatalog catalog,
        ExternalPluginTrustManager trust,
        IPluginBrokerProbeClient broker,
        PluginBrokerQuarantineManager quarantine)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(quarantine);
        _catalog = catalog;
        _trust = trust;
        _broker = broker;
        _quarantine = quarantine;
    }

    public async Task<PluginBrokerResponse> ProbeAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        _quarantine.EnsureProbeAllowed(pluginId);

        var candidates = await _catalog.ScanAsync(cancellationToken).ConfigureAwait(false);
        var matches = candidates
            .Where(item => string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                "The external plugin candidate is no longer uniquely available.");
        }

        var candidate = matches[0];
        if (!_trust.IsApproved(candidate) ||
            candidate.Status != ExternalPluginCandidateStatus.ReadyForConsent ||
            candidate.AssemblySha256 is null ||
            candidate.SignerCertificateSha256 is null ||
            candidate.PackageDirectoryPath is null ||
            candidate.EntryAssemblyPath is null)
        {
            throw new InvalidOperationException(
                "The external plugin must pass a fresh trust scan and exact capability consent before probing.");
        }

        var issuedAtUtc = DateTimeOffset.UtcNow;
        var grant = new PluginBrokerGrant(
            candidate.Id!,
            candidate.PackageDirectoryPath,
            candidate.EntryAssemblyPath,
            candidate.AssemblySha256,
            candidate.SignerCertificateSha256,
            (int)candidate.Capabilities,
            issuedAtUtc,
            issuedAtUtc + GrantLifetime,
            candidate.Dependencies ?? [],
            candidate.EntryType);
        try
        {
            var response = await _broker.ProbeMetadataAsync(grant, cancellationToken)
                .ConfigureAwait(false);
            _quarantine.RecordSuccess(candidate.Id!);
            return response;
        }
        catch (Exception exception) when (CountsTowardQuarantine(exception))
        {
            _quarantine.RecordFailure(candidate.Id!);
            throw;
        }
    }

    private static bool CountsTowardQuarantine(Exception exception) =>
        exception is not FileNotFoundException &&
        (exception is TimeoutException or InvalidDataException or IOException);
}
