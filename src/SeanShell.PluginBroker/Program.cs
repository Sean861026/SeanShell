using SeanShell.PluginBroker;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

return await PluginBrokerEntryPoint.RunAsync(
    args,
    Console.In,
    Console.Out,
    Environment.ProcessId,
    cancellation.Token);
