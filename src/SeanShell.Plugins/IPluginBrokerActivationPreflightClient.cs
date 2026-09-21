using SeanShell.PluginBroker.Protocol;

namespace SeanShell.Plugins;

public interface IPluginBrokerActivationPreflightClient : IPluginBrokerProbeClient
{
    Task<PluginBrokerResponse> PreflightActivationAsync(
        PluginBrokerGrant grant,
        PluginBrokerActivationRequest activation,
        CancellationToken cancellationToken = default);
}
