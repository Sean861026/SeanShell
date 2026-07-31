namespace SeanShell.Core;

public static class DockControlMotion
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
            ? new DockItemMotionState(1.03f, 0, 140)
            : new DockItemMotionState(1, 0, 120);
    }
}
