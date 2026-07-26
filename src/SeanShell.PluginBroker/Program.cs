using System.Security.Cryptography;
using SeanShell.PluginBroker;
using SeanShell.PluginBroker.Protocol;

BrokerProcessMitigations.Apply();
var sessionKey = BrokerSessionKeyReader.Read(args);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

try
{
    var response = await PluginBrokerSession.RunAsync(
        Console.In,
        Console.Out,
        Environment.ProcessId,
        sessionKey,
        cancellation.Token);
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
