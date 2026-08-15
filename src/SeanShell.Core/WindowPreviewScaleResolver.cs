namespace SeanShell.Core;

public static class WindowPreviewScaleResolver
{
    public static double Resolve(
        double displayScaleFactor,
        double? xamlRootScaleFactor)
    {
        if (IsValid(displayScaleFactor))
        {
            return displayScaleFactor;
        }

        return xamlRootScaleFactor is double xamlScale && IsValid(xamlScale)
            ? xamlScale
            : 1;
    }

    private static bool IsValid(double scaleFactor) =>
        double.IsFinite(scaleFactor) && scaleFactor > 0;
}
