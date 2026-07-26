namespace SeanShell.Plugins;

public sealed record PluginBrokerQuarantineEntry(
    string PluginId,
    int ConsecutiveFailures,
    DateTimeOffset WindowStartedAtUtc,
    DateTimeOffset LastFailureAtUtc,
    DateTimeOffset? QuarantinedUntilUtc = null);

public sealed record PluginBrokerQuarantineDocument(
    int SchemaVersion = 1,
    IReadOnlyList<PluginBrokerQuarantineEntry>? Entries = null)
{
    public const int CurrentSchemaVersion = 1;

    public IReadOnlyList<PluginBrokerQuarantineEntry> EffectiveEntries => Entries ?? [];
}

public sealed record PluginBrokerQuarantineLoadResult(
    PluginBrokerQuarantineDocument Document,
    bool PersistenceAvailable = true,
    bool WasRecovered = false,
    string? Warning = null);

public sealed record PluginBrokerQuarantineStatus(
    string PluginId,
    int ConsecutiveFailures,
    DateTimeOffset? QuarantinedUntilUtc)
{
    public bool IsQuarantined(DateTimeOffset currentTimeUtc) =>
        QuarantinedUntilUtc > currentTimeUtc;
}

public sealed class PluginBrokerQuarantinedException(
    string pluginId,
    DateTimeOffset quarantinedUntilUtc)
    : InvalidOperationException(
        $"External plugin '{pluginId}' is quarantined until {quarantinedUntilUtc:O} after repeated broker failures.")
{
    public string PluginId { get; } = pluginId;

    public DateTimeOffset QuarantinedUntilUtc { get; } = quarantinedUntilUtc;
}
