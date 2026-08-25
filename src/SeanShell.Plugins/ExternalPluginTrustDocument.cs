namespace SeanShell.Plugins;

public sealed record ExternalPluginTrustDocument(
    int SchemaVersion = 2,
    IReadOnlyList<ExternalPluginConsent>? Consents = null)
{
    public const int CurrentSchemaVersion = 2;

    public IReadOnlyList<ExternalPluginConsent> EffectiveConsents => Consents ?? [];
}

public sealed record ExternalPluginTrustLoadResult(
    ExternalPluginTrustDocument Document,
    bool WasRecovered = false,
    string? Warning = null);
