namespace SeanShell.Core;

public static class WindowPreviewEntranceMotion
{
    public static WindowPreviewEntranceMotionState Resolve(bool reducedEffects) =>
        reducedEffects
            ? new WindowPreviewEntranceMotionState(1, 1, 0, 0, 0)
            : new WindowPreviewEntranceMotionState(0, 1, 8, 0, 160);
}

public sealed record WindowPreviewEntranceMotionState(
    double StartOpacity,
    double EndOpacity,
    double StartTranslationY,
    double EndTranslationY,
    int DurationMilliseconds);
