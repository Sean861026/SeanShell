namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerCommandQuery(
    string Query,
    int MaximumResults);
