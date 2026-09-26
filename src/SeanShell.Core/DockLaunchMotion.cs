namespace SeanShell.Core;

public static class DockLaunchMotion
{
    private static readonly IReadOnlyList<DockLaunchMotionFrame> StandardFrames =
    [
        new(0, 1),
        new(0.24f, 0.6f),
        new(0.50f, 1),
        new(0.72f, 0.8f),
        new(1, 1),
    ];

    public static DockLaunchMotionState Resolve(bool reducedEffects) =>
        reducedEffects
            ? new DockLaunchMotionState(0, [new(0, 1)])
            : new DockLaunchMotionState(420, StandardFrames);
}

public sealed record DockLaunchMotionState(
    int DurationMilliseconds,
    IReadOnlyList<DockLaunchMotionFrame> Frames);

public sealed record DockLaunchMotionFrame(
    float Progress,
    float Opacity);
