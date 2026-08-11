namespace SeanShell.Core;

public static class DockVisibilityMotion
{
    public static DockVisibilityMotionState Resolve(
        bool collapsed,
        bool reducedEffects)
    {
        if (reducedEffects)
        {
            return new DockVisibilityMotionState(1, 1, 0, 0, 0);
        }

        return collapsed
            ? new DockVisibilityMotionState(1, 0, 0, 12, 120)
            : new DockVisibilityMotionState(0, 1, 12, 0, 180);
    }
}

public sealed record DockVisibilityMotionState(
    double StartOpacity,
    double EndOpacity,
    double StartTranslationY,
    double EndTranslationY,
    int DurationMilliseconds);
