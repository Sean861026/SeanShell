using System.Text.Json;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerSession
{
    public static async Task<PluginBrokerResponse> RunAsync(
        TextReader input,
        TextWriter output,
        int processId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        PluginBrokerResponse response;
        try
        {
            var frame = await PluginBrokerProtocol.ReadFrameAsync(input, cancellationToken)
                .ConfigureAwait(false);
            var request = PluginBrokerProtocol.DeserializeRequest(frame);
            response = Handle(request, processId);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or EndOfStreamException or ArgumentException)
        {
            response = new PluginBrokerResponse(
                PluginBrokerProtocol.CurrentVersion,
                string.Empty,
                false,
                $"Rejected request: {exception.Message}",
                processId);
        }

        await output.WriteLineAsync(PluginBrokerProtocol.Serialize(response)).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static PluginBrokerResponse Handle(PluginBrokerRequest request, int processId)
    {
        if (request.ProtocolVersion != PluginBrokerProtocol.CurrentVersion)
        {
            return Reject(
                request,
                processId,
                $"Unsupported protocol version {request.ProtocolVersion}.");
        }

        if (!PluginBrokerProtocol.IsValidRequestId(request.RequestId))
        {
            return Reject(request, processId, "Request ID must be a 32-character GUID.");
        }

        if (!string.Equals(
                request.Operation,
                PluginBrokerProtocol.HealthOperation,
                StringComparison.Ordinal))
        {
            return Reject(
                request,
                processId,
                "The requested operation is not enabled.");
        }

        return new PluginBrokerResponse(
            PluginBrokerProtocol.CurrentVersion,
            request.RequestId,
            true,
            "Broker handshake ready; external activation is disabled.",
            processId);
    }

    private static PluginBrokerResponse Reject(
        PluginBrokerRequest request,
        int processId,
        string status) =>
        new(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.IsValidRequestId(request.RequestId)
                ? request.RequestId
                : string.Empty,
            false,
            status,
            processId);
}
