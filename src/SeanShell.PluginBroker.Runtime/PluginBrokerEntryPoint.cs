using System.Security.Cryptography;
using SeanShell.PluginBroker.Protocol;
using SeanShell.PluginBroker.Runtime;

namespace SeanShell.PluginBroker;

public static class PluginBrokerEntryPoint
{
    public const string BrokerModeArgument = "--plugin-broker";

    public static bool IsBrokerModeRequested(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Length > 0 &&
               string.Equals(
                   arguments[0],
                   BrokerModeArgument,
                   StringComparison.Ordinal);
    }

    public static async Task<int> RunAsync(
        string[] arguments,
        TextReader input,
        TextWriter output,
        int processId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        BrokerProcessMitigations.Apply();
        var sessionKey = BrokerSessionKeyReader.Read(arguments);
        try
        {
            var response = await PluginBrokerSession.RunAsync(
                input,
                output,
                processId,
                sessionKey,
                cancellationToken,
                entryPointValidator: PluginEntryPointInspector.Validate).ConfigureAwait(false);
            return response.Accepted ? 0 : 2;
        }
        catch (OperationCanceledException)
        {
            return 3;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKey);
        }
    }
}
