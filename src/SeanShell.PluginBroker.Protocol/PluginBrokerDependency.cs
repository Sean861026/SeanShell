namespace SeanShell.PluginBroker.Protocol;

public sealed record PluginBrokerDependency(
    string RelativePath,
    string Sha256,
    string Kind);
