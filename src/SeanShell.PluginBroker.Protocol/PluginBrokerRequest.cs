namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerRequest(
    int ProtocolVersion,
    string RequestId,
    string Operation,
    PluginBrokerGrant? Grant = null,
    string SessionId = "",
    string Nonce = "",
    string? AuthenticationTag = null);
