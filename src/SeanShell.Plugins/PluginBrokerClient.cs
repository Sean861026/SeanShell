using System.Diagnostics;
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
        if (!File.Exists(_brokerExecutablePath))
        {
            throw new FileNotFoundException(
                "The SeanShell plugin broker executable is unavailable.",
                _brokerExecutablePath);
        }

        var request = new PluginBrokerRequest(
            PluginBrokerProtocol.CurrentVersion,
            PluginBrokerProtocol.CreateRequestId(),
            PluginBrokerProtocol.HealthOperation);
        using var timeoutCancellation = new CancellationTokenSource(_timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_brokerExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            },
        };
        var started = false;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The plugin broker could not be started.");
            }

            started = true;
            await process.StandardInput.WriteLineAsync(PluginBrokerProtocol.Serialize(request))
                .ConfigureAwait(false);
            process.StandardInput.Close();
            var frame = await PluginBrokerProtocol.ReadFrameAsync(
                process.StandardOutput,
                linkedCancellation.Token).ConfigureAwait(false);
            var response = PluginBrokerProtocol.DeserializeResponse(frame);
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);

            if (process.ExitCode != 0 ||
                !response.Accepted ||
                response.ProtocolVersion != PluginBrokerProtocol.CurrentVersion ||
                !string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal) ||
                response.BrokerProcessId != process.Id)
            {
                throw new InvalidDataException(
                    $"The plugin broker handshake was rejected. {response.Status}");
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
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }
}
