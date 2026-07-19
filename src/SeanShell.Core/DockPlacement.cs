namespace SeanShell.Core;

public static class DockPlacement
{
    private const int ExpandedEdgeOffset = 12;
    private const int MinimumHorizontalMargin = 8;

    public static DockBounds Calculate(
        DisplayMonitorSnapshot monitor,
        int desiredWidth,
        int desiredHeight,
        bool collapsed,
        int peekWidth,
        int peekHeight)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desiredWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desiredHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peekWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peekHeight);

        var availableWidth = Math.Max(1, monitor.WorkAreaWidth - (MinimumHorizontalMargin * 2));
        var width = Math.Min(collapsed ? peekWidth : desiredWidth, availableWidth);
        var height = Math.Min(collapsed ? peekHeight : desiredHeight, monitor.WorkAreaHeight);
        var x = monitor.WorkAreaX + Math.Max(0, (monitor.WorkAreaWidth - width) / 2);
        var offset = collapsed ? 0 : ExpandedEdgeOffset;
        var y = monitor.WorkAreaY + Math.Max(0, monitor.WorkAreaHeight - height - offset);

        return new DockBounds(x, y, width, height);
    }
}

public sealed record DockBounds(int X, int Y, int Width, int Height);
