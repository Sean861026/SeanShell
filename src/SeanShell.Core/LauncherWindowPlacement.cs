namespace SeanShell.Core;

public sealed record LauncherWindowBounds(int X, int Y, int Width, int Height);

public static class LauncherWindowPlacement
{
    private const int EdgeMargin = 24;

    public static LauncherWindowBounds Calculate(
        int workAreaX,
        int workAreaY,
        int workAreaWidth,
        int workAreaHeight,
        int desiredWidth,
        int desiredHeight,
        double scaleFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workAreaWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workAreaHeight);

        var margin = DisplayScaleLayout.ToPhysicalPixels(EdgeMargin, scaleFactor);
        var targetWidth = DisplayScaleLayout.ToPhysicalPixels(desiredWidth, scaleFactor);
        var targetHeight = DisplayScaleLayout.ToPhysicalPixels(desiredHeight, scaleFactor);
        var availableWidth = Math.Max(1, workAreaWidth - (margin * 2));
        var availableHeight = Math.Max(1, workAreaHeight - (margin * 2));
        var width = Math.Min(targetWidth, availableWidth);
        var height = Math.Min(targetHeight, availableHeight);
        var x = workAreaX + ((workAreaWidth - width) / 2);
        var maximumTop = workAreaHeight - height - margin;
        var preferredTop = (workAreaHeight - height) / 3;
        var y = workAreaY + Math.Clamp(preferredTop, margin, Math.Max(margin, maximumTop));

        return new LauncherWindowBounds(x, y, width, height);
    }
}
