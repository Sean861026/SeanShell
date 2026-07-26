using SeanShell.PluginBroker.Protocol;

namespace SeanShell.Plugins;

public sealed class PluginBrokerClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly string _brokerExecutablePath;
    private readonly TimeSpan _timeout;

    public PluginBrokerClient(string brokerExecutablePath, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerExecutablePath);
        _brokerExecutablePath = Path.GetFullPath(brokerExecutablePath);
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<PluginBrokerResponse> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new PluginBrokerRequest(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            PluginBrokerProtocol.HealthOperation);
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PluginBrokerResponse> ProbeMetadataAsync(
        PluginBrokerGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var request = new PluginBrokerRequest(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            PluginBrokerProtocol.MetadataProbeOperation,
            grant);
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Metadata is null ||
            !string.Equals(response.Metadata.PluginId, grant.PluginId, StringComparison.Ordinal) ||
            !string.Equals(
                response.Metadata.AssemblySha256,
                grant.AssemblySha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                response.Metadata.PublisherCertificateSha256,
                grant.PublisherCertificateSha256,
                StringComparison.OrdinalIgnoreCase) ||
            response.Metadata.GrantedCapabilities != grant.GrantedCapabilities)
        {
            throw new InvalidDataException(
                "The plugin broker returned metadata that does not match the capability grant.");
        }

        return response;
    }

    private async Task<PluginBrokerResponse> SendAsync(
        PluginBrokerRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_brokerExecutablePath))
        {
            throw new FileNotFoundException(
                "The SeanShell plugin broker executable is unavailable.",
                _brokerExecutablePath);
        }

        using var timeoutCancellation = new CancellationTokenSource(_timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        using var sandbox = BrokerProcessSandbox.Create();
        using var process = SuspendedBrokerProcess.Start(_brokerExecutablePath, sandbox);

        try
        {
            await process.Input.WriteLineAsync(PluginBrokerProtocol.Serialize(request))
                .ConfigureAwait(false);
            process.Input.Close();
            var frameTask = PluginBrokerProtocol.ReadFrameAsync(
                process.Output,
                linkedCancellation.Token);
            var frame = await frameTask.WaitAsync(linkedCancellation.Token)
                .ConfigureAwait(false);
            var response = PluginBrokerProtocol.DeserializeResponse(frame);
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);

            if (process.ExitCode != 0 ||
                !response.Accepted ||
                response.ProtocolVersion != PluginBrokerProtocol.CurrentVersion ||
                !string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal) ||
                response.BrokerProcessId != process.Id)
            {
                throw new InvalidDataException(
                    $"The plugin broker request was rejected. {response.Status}");
            }

            return response;
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The plugin broker did not respond within {_timeout.TotalMilliseconds:F0} ms.");
        }
        finally
        {
            process.Terminate();
        }
    }
}
