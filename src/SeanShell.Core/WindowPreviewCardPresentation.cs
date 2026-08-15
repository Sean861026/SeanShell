namespace SeanShell.Core;

public static class WindowPreviewCardPresentation
{
    public static WindowPreviewCardVisualState Resolve(
        bool isMinimized,
        bool isForeground)
    {
        if (isMinimized)
        {
            return new WindowPreviewCardVisualState("Minimized", false);
        }

        return isForeground
            ? new WindowPreviewCardVisualState("Active", true)
            : new WindowPreviewCardVisualState("Running", false);
    }
}

public sealed record WindowPreviewCardVisualState(
    string StatusLabel,
    bool UsesAccentStroke);
