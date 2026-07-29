namespace SeanShell.Core;

public static class DockForegroundContinuity
{
    public static IReadOnlyList<DesktopWindowSnapshot> Apply(
        IReadOnlyList<DesktopWindowSnapshot> windows,
        nint previousForegroundWindow)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (previousForegroundWindow == 0 ||
            windows.Any(static window => window.IsForeground) ||
            !windows.Any(window => window.Handle == previousForegroundWindow))
        {
            return windows;
        }

        return windows
            .Select(window => window.Handle == previousForegroundWindow
                ? window with { IsForeground = true }
                : window)
            .ToArray();
    }
}
