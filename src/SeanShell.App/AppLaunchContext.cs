using Microsoft.Windows.AppLifecycle;

namespace SeanShell.App;

internal static class AppLaunchContext
{
    public static bool IsAutomaticStartup { get; set; }

    public static bool DetectAutomaticStartup()
    {
        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind ==
                ExtendedActivationKind.StartupTask;
        }
        catch
        {
            // Identity-free broker and development launches remain ordinary manual launches.
            return false;
        }
    }
}
