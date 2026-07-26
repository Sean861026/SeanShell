using SeanShell.PluginBroker.Protocol;
using SeanShell.PluginBroker;

BrokerProcessMitigations.Apply();

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
        cancellation.Token);
    return response.Accepted ? 0 : 2;
}
catch (OperationCanceledException)
{
    return 3;
}
