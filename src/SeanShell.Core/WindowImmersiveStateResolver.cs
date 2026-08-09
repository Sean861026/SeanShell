namespace SeanShell.Core;

public static class WindowImmersiveStateResolver
{
    public static bool IsImmersive(
        bool isMaximized,
        DockBounds windowBounds,
        DockBounds monitorBounds,
        int tolerance = 2)
    {
        if (isMaximized)
        {
            return true;
        }

        if (windowBounds.Width <= 0 ||
            windowBounds.Height <= 0 ||
            monitorBounds.Width <= 0 ||
            monitorBounds.Height <= 0)
        {
            return false;
        }

        tolerance = Math.Max(0, tolerance);
        return windowBounds.X <= monitorBounds.X + tolerance &&
            windowBounds.Y <= monitorBounds.Y + tolerance &&
            windowBounds.X + windowBounds.Width >=
                monitorBounds.X + monitorBounds.Width - tolerance &&
            windowBounds.Y + windowBounds.Height >=
                monitorBounds.Y + monitorBounds.Height - tolerance;
    }
}
