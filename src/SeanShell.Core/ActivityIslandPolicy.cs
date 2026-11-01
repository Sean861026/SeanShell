namespace SeanShell.Core;

public static class ActivityIslandPolicy
{
    public static bool CanShow(bool enabled, bool gaming, bool immersive, bool shuttingDown) =>
        enabled && !gaming && !immersive && !shuttingDown;
}
