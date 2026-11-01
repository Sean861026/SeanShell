namespace SeanShell.Core;

public sealed record ActivityIslandBounds(int X, int Y, int Width, int Height);

public static class ActivityIslandLayout
{
    public const int DesiredWidth = 360;
    public const int DesiredHeight = 76;
    public const int EdgeMargin = 16;

    public static ActivityIslandBounds Calculate(
        DisplayMonitorSnapshot monitor,
        double scaleFactor,
        double textScaleFactor = 1)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monitor.WorkAreaWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monitor.WorkAreaHeight);
        if (!double.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }
        if (!double.IsFinite(textScaleFactor) || textScaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textScaleFactor));
        }

        if ((long)monitor.WorkAreaX + monitor.WorkAreaWidth - 1 > int.MaxValue ||
            (long)monitor.WorkAreaY + monitor.WorkAreaHeight - 1 > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(monitor));
        }

        // Shrink each margin independently on tiny work areas, always leaving
        // at least one physical pixel. Clamp before converting to avoid overflow
        // even when a finite positive scale is much larger than a real DPI.
        var horizontalMargin = ScaleWithin(
            EdgeMargin, scaleFactor, (monitor.WorkAreaWidth - 1) / 2);
        var verticalMargin = ScaleWithin(
            EdgeMargin, scaleFactor, (monitor.WorkAreaHeight - 1) / 2);
        var width = ScaleWithin(
            DesiredWidth, scaleFactor, monitor.WorkAreaWidth - (2 * horizontalMargin));
        var height = ScaleWithin(
            DesiredHeight, scaleFactor * Math.Max(1, textScaleFactor),
            monitor.WorkAreaHeight - (2 * verticalMargin));

        return new ActivityIslandBounds(
            monitor.WorkAreaX + ((monitor.WorkAreaWidth - width) / 2),
            monitor.WorkAreaY + verticalMargin,
            width,
            height);
    }

    private static int ScaleWithin(int logicalPixels, double scaleFactor, int limit) =>
        (int)Math.Min(limit, Math.Ceiling(logicalPixels * scaleFactor));
}
