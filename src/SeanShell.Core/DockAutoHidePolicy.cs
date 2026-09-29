namespace SeanShell.Core;

public static class DockAutoHidePolicy
{
    public static bool CanCollapse(
        bool enabled,
        bool pointerInside,
        bool hasKeyboardFocus,
        bool interactiveSurfaceOpen,
        bool gamingMode) =>
        enabled &&
        !pointerInside &&
        !hasKeyboardFocus &&
        !interactiveSurfaceOpen &&
        !gamingMode;
}
