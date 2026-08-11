using Microsoft.Windows.AppLifecycle;

namespace SeanShell.App;

internal static class AppLaunchContext
{
    public const string MainInstanceKey = "SeanShell.Main";

    public static AppInstance? MainInstance { get; set; }

    public static bool IsAutomaticStartup { get; set; }

    public static bool HasAutomaticStartupArgument() =>
        Environment.GetCommandLineArgs()
            .Skip(1)
            .Contains("--startup", StringComparer.OrdinalIgnoreCase) ||
        Environment.CommandLine.Contains(
            "--startup",
            StringComparison.OrdinalIgnoreCase);

    public static bool DetectAutomaticStartup(AppActivationArguments? activation) =>
        activation?.Kind == ExtendedActivationKind.StartupTask;
}
