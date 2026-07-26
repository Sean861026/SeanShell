namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerCommandInvocation(
    string CommandId,
    string CommandSetSha256);
