namespace SeanShell.Core;

public static class DockLaunchMotion
{
    private static readonly IReadOnlyList<DockLaunchMotionFrame> StandardFrames =
    [
        new(0, 0),
        new(0.24f, -14),
        new(0.50f, 0),
        new(0.72f, -7),
        new(1, 0),
    ];

    public static DockLaunchMotionState Resolve(bool reducedEffects) =>
        reducedEffects
            ? new DockLaunchMotionState(0, [new(0, 0)])
            : new DockLaunchMotionState(420, StandardFrames);
}

public sealed record DockLaunchMotionState(
    int DurationMilliseconds,
    IReadOnlyList<DockLaunchMotionFrame> Frames);

public sealed record DockLaunchMotionFrame(
    float Progress,
    float TranslationY);
