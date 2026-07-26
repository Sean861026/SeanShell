namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerCommandDescriptor(
    string Id,
    string Title,
    string? Subtitle,
    string[] Keywords);
