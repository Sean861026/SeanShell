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
            return new DockItemMotionState(0.92f, 0, 90);
        }

        return isPointerOver
            ? new DockItemMotionState(1.35f, -6, 220)
            : new DockItemMotionState(1, 0, 180);
    }

    public static DockItemMotionState ResolveNeighbor(
        bool isHighlighted,
        bool reducedEffects)
    {
        if (reducedEffects)
        {
            return new DockItemMotionState(1, 0, 0);
        }

        return isHighlighted
            ? new DockItemMotionState(1.14f, -2, 180)
            : new DockItemMotionState(1, 0, 160);
    }
}

public sealed record DockItemMotionState(
    float Scale,
    float TranslationY,
    int DurationMilliseconds);
