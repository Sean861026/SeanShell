using SeanShell.PluginBroker.Protocol;

namespace SeanShell.Plugins;

public interface IPluginBrokerProbeClient
{
    Task<PluginBrokerResponse> ProbeMetadataAsync(
        PluginBrokerGrant grant,
        CancellationToken cancellationToken = default);
}
