namespace SeanShell.Core;

public static class TaskbarDockLayout
{
    public const int PreferredMinimumWidth = 420;
    public const int PreferredMaximumWidth = 1000;
    public const int MonitorEdgeMargin = 32;

    private const int FixedControlsWidth = 256;
    private const int ItemSlotWidth = 52;

    public static int CalculateExpandedWidth(
        int pinnedItemCount,
        int windowItemCount,
        int monitorWorkAreaWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pinnedItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(windowItemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monitorWorkAreaWidth);

        var availableWidth = Math.Max(
            1,
            monitorWorkAreaWidth - MonitorEdgeMargin);
        var preferredWidth = checked(
            FixedControlsWidth +
            ((pinnedItemCount + windowItemCount) * ItemSlotWidth));
        var minimumWidth = Math.Min(
            PreferredMinimumWidth,
            availableWidth);

        return Math.Clamp(
            preferredWidth,
            minimumWidth,
            Math.Min(PreferredMaximumWidth, availableWidth));
    }
}
