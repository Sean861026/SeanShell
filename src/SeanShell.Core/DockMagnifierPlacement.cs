namespace SeanShell.Core;

public static class DockMagnifierPlacement
{
    public static DockMagnifierBounds Calculate(
        int anchorCenterX,
        int anchorBottomY,
        DisplayMonitorSnapshot monitor,
        int desiredWidth,
        int desiredHeight,
        double scaleFactor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (desiredWidth <= 0 || desiredHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredWidth),
                "Magnifier dimensions must be positive.");
        }

        var width = Math.Min(
            DisplayScaleLayout.ToPhysicalPixels(desiredWidth, scaleFactor),
            monitor.WorkAreaWidth);
        var height = Math.Min(
            DisplayScaleLayout.ToPhysicalPixels(desiredHeight, scaleFactor),
            monitor.WorkAreaHeight);
        var maximumX = monitor.WorkAreaX + monitor.WorkAreaWidth - width;
        var maximumY = monitor.WorkAreaY + monitor.WorkAreaHeight - height;
        var x = Math.Clamp(anchorCenterX - (width / 2), monitor.WorkAreaX, maximumX);
        var y = Math.Clamp(anchorBottomY - height, monitor.WorkAreaY, maximumY);
        return new DockMagnifierBounds(x, y, width, height);
    }
}

public sealed record DockMagnifierBounds(int X, int Y, int Width, int Height);
