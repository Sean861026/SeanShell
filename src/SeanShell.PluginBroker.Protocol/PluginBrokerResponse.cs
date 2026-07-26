namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerResponse(
    int ProtocolVersion,
    string RequestId,
    bool Accepted,
    string Status,
    int BrokerProcessId,
    PluginBrokerMetadata? Metadata = null,
    string SessionId = "",
    string Nonce = "",
    string? AuthenticationTag = null);
