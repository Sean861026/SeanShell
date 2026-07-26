namespace SeanShell.Core;

public sealed record SystemAccessibilitySnapshot(
    bool AnimationsEnabled,
    double TextScaleFactor)
{
    public bool ReducedEffects => !AnimationsEnabled;
}

public static class AccessibilityLayout
{
    public static int ScaleDockHeight(int baseHeight, double textScaleFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseHeight);
        if (!double.IsFinite(textScaleFactor) || textScaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textScaleFactor));
        }

        var layoutScale = 1 + ((Math.Clamp(textScaleFactor, 1, 2.25) - 1) * 0.4);
        return (int)Math.Ceiling(baseHeight * layoutScale);
    }
}
