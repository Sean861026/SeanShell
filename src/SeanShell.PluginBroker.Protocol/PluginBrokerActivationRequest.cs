namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerActivationRequest(
    string EntryType,
    int RequestedCapabilities);
