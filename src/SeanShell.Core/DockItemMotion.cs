namespace SeanShell.Core;

public static class DockItemMotion
{
    public static DockItemMotionState Resolve(
        bool isPointerOver,
        bool isPressed,
        bool reducedEffects)
    {
        if (reducedEffects)
        {
            return new DockItemMotionState(1, 0, 0);
        }

        if (isPressed)
        {
            return new DockItemMotionState(0.97f, 0, 80);
        }

        return isPointerOver
            ? new DockItemMotionState(1.03f, -1, 140)
            : new DockItemMotionState(1, 0, 120);
    }
}

public sealed record DockItemMotionState(
    float Scale,
    float TranslationY,
    int DurationMilliseconds);
