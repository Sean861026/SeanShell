namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerRequest(
    int ProtocolVersion,
    string RequestId,
    string Operation);
