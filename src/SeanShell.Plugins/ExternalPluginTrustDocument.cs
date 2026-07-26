namespace SeanShell.Plugins;

public sealed record ExternalPluginTrustDocument(
    int SchemaVersion = 1,
    IReadOnlyList<ExternalPluginConsent>? Consents = null)
{
    public const int CurrentSchemaVersion = 1;

    public IReadOnlyList<ExternalPluginConsent> EffectiveConsents => Consents ?? [];
}

public sealed record ExternalPluginTrustLoadResult(
    ExternalPluginTrustDocument Document,
    bool WasRecovered = false,
    string? Warning = null);
