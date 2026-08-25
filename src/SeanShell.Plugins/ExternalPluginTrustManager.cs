using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed class ExternalPluginTrustManager
{
    private readonly ExternalPluginTrustStore _store;
    private ExternalPluginTrustDocument _document;

    public ExternalPluginTrustManager(
        ExternalPluginTrustStore store,
        ExternalPluginTrustLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loadResult);
        _store = store;
        _document = loadResult.Document;
        Warning = loadResult.Warning;
    }

    public string? Warning { get; }

    public IReadOnlyList<ExternalPluginConsent> Consents => _document.EffectiveConsents;

    public bool IsApproved(ExternalPluginCandidate candidate)
    {
        if (candidate.Status != ExternalPluginCandidateStatus.ReadyForConsent ||
            candidate.Capabilities == PluginCapability.None)
        {
            return false;
        }

        var consent = Find(candidate);
        return consent is not null &&
               string.Equals(
                   consent.EntryType,
                   candidate.EntryType,
                   StringComparison.Ordinal) &&
               (consent.GrantedCapabilities & candidate.Capabilities) == candidate.Capabilities;
    }

    public void Approve(ExternalPluginCandidate candidate, DateTimeOffset grantedAtUtc)
    {
        ValidateApprovableCandidate(candidate);
        if (grantedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(grantedAtUtc));
        }

        var fingerprint = ExternalPluginTrustStore.NormalizeFingerprint(
            candidate.SignerCertificateSha256!);
        var consents = _document.EffectiveConsents
            .Where(consent => !Matches(consent, candidate.Id!, fingerprint))
            .Append(new ExternalPluginConsent(
                candidate.Id!,
                fingerprint,
                candidate.Capabilities,
                grantedAtUtc.ToUniversalTime(),
                candidate.EntryType))
            .OrderBy(static consent => consent.PluginId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static consent => consent.PublisherCertificateSha256, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SaveThenApply(new ExternalPluginTrustDocument(Consents: consents));
    }

    public void Revoke(ExternalPluginCandidate candidate)
    {
        if (candidate.Id is null || candidate.SignerCertificateSha256 is null)
        {
            return;
        }

        var fingerprint = ExternalPluginTrustStore.NormalizeFingerprint(
            candidate.SignerCertificateSha256);
        var consents = _document.EffectiveConsents
            .Where(consent => !Matches(consent, candidate.Id, fingerprint))
            .ToArray();
        if (consents.Length == _document.EffectiveConsents.Count)
        {
            return;
        }

        SaveThenApply(new ExternalPluginTrustDocument(Consents: consents));
    }

    public void RevokeAll()
    {
        if (_document.EffectiveConsents.Count == 0)
        {
            return;
        }

        SaveThenApply(new ExternalPluginTrustDocument());
    }

    private ExternalPluginConsent? Find(ExternalPluginCandidate candidate)
    {
        if (candidate.Id is null || candidate.SignerCertificateSha256 is null)
        {
            return null;
        }

        var fingerprint = ExternalPluginTrustStore.NormalizeFingerprint(
            candidate.SignerCertificateSha256);
        return _document.EffectiveConsents.FirstOrDefault(
            consent => Matches(consent, candidate.Id, fingerprint));
    }

    private static bool Matches(
        ExternalPluginConsent consent,
        string pluginId,
        string fingerprint) =>
        string.Equals(consent.PluginId, pluginId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            ExternalPluginTrustStore.NormalizeFingerprint(consent.PublisherCertificateSha256),
            fingerprint,
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateApprovableCandidate(ExternalPluginCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Status != ExternalPluginCandidateStatus.ReadyForConsent ||
            candidate.Id is null ||
            candidate.SignerCertificateSha256 is null ||
            candidate.Capabilities == PluginCapability.None)
        {
            throw new InvalidOperationException(
                "Only a signed candidate that passed all trust checks can receive capability consent.");
        }
    }

    private void SaveThenApply(ExternalPluginTrustDocument document)
    {
        _store.Save(document);
        _document = document;
    }
}
