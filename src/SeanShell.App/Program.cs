using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
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
        var currentInstance = AppInstance.GetCurrent();
        var activation = currentInstance.GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(
            AppLaunchContext.MainInstanceKey);
        if (!mainInstance.IsCurrent)
        {
            if (activation is not null)
            {
                mainInstance.RedirectActivationToAsync(activation)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            return 0;
        }

        AppLaunchContext.MainInstance = mainInstance;
        AppLaunchContext.IsAutomaticStartup =
            AppLaunchContext.DetectAutomaticStartup(activation) ||
            AppLaunchContext.HasAutomaticStartupArgument();
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
