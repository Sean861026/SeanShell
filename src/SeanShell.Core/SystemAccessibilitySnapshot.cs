namespace SeanShell.Core;

public sealed record SystemAccessibilitySnapshot(
    bool AnimationsEnabled,
    double TextScaleFactor,
    bool HighContrast)
{
    public bool ReducedEffects => !AnimationsEnabled || HighContrast;
}

public static class AccessibilityLayout
{
    private const double MaximumTextScaleFactor = 2.25;
    private const double LayoutScaleWeight = 0.4;

    public static int ScaleDockHeight(int baseHeight, double textScaleFactor)
        => ScaleDimension(baseHeight, textScaleFactor);

    public static int ScaleDockFixedControlsWidth(
        int baseWidth,
        double textScaleFactor)
        => ScaleDimension(baseWidth, textScaleFactor);

    private static int ScaleDimension(int baseDimension, double textScaleFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseDimension);
        if (!double.IsFinite(textScaleFactor) || textScaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textScaleFactor));
        }

        var layoutScale =
            1 +
            ((Math.Clamp(textScaleFactor, 1, MaximumTextScaleFactor) - 1) *
             LayoutScaleWeight);
        return (int)Math.Ceiling(baseDimension * layoutScale);
    }
}
