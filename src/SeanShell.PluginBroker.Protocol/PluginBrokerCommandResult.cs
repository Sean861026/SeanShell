namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerCommandResult(
    string Outcome,
    string? Message);
