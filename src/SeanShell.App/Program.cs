using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SeanShell.PluginBroker;
using SeanShell.Windows;

namespace SeanShell.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (TaskbarRecoveryEntryPoint.IsGuardModeRequested(args))
        {
            return TaskbarRecoveryEntryPoint.Run(args);
        }

        if (PluginBrokerEntryPoint.IsBrokerModeRequested(args))
        {
            return PluginBrokerEntryPoint.RunAsync(
                    args,
                    Console.In,
                    Console.Out,
                    Environment.ProcessId)
                .GetAwaiter()
                .GetResult();
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppLaunchContext.IsAutomaticStartup = AppLaunchContext.DetectAutomaticStartup();
        Application.Start(initialization =>
        {
            _ = initialization;
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
