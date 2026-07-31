namespace SeanShell.Core;

public static class DockOverflowNavigation
{
    private const double BoundaryTolerance = 0.5;
    private const double MinimumPageSize = 52;

    public static DockOverflowState Resolve(
        double horizontalOffset,
        double scrollableWidth)
    {
        Validate(horizontalOffset, nameof(horizontalOffset));
        Validate(scrollableWidth, nameof(scrollableWidth));

        var isVisible = scrollableWidth > BoundaryTolerance;
        return new DockOverflowState(
            isVisible,
            isVisible && horizontalOffset > BoundaryTolerance,
            isVisible && horizontalOffset < scrollableWidth - BoundaryTolerance);
    }

    public static double CalculateTargetOffset(
        double horizontalOffset,
        double viewportWidth,
        double scrollableWidth,
        DockOverflowDirection direction)
    {
        Validate(horizontalOffset, nameof(horizontalOffset));
        Validate(viewportWidth, nameof(viewportWidth));
        Validate(scrollableWidth, nameof(scrollableWidth));
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var pageSize = Math.Max(MinimumPageSize, viewportWidth * 0.75);
        var delta = direction == DockOverflowDirection.Previous
            ? -pageSize
            : pageSize;
        return Math.Clamp(horizontalOffset + delta, 0, scrollableWidth);
    }

    private static void Validate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public enum DockOverflowDirection
{
    Previous,
    Next,
}

public sealed record DockOverflowState(
    bool IsVisible,
    bool CanNavigatePrevious,
    bool CanNavigateNext);
