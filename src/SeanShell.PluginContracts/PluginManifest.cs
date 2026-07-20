namespace SeanShell.PluginContracts;

public sealed record PluginManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string Version,
    int MinimumHostApiVersion,
    string Publisher,
    PluginCapability Capabilities,
    bool IsBuiltIn)
{
    public const int CurrentSchemaVersion = 1;
}
