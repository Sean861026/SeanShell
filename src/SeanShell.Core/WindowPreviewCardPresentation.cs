namespace SeanShell.Core;

public static class WindowPreviewCardPresentation
{
    public static WindowPreviewCardVisualState Resolve(
        bool isMinimized,
        bool isForeground)
    {
        if (isMinimized)
        {
            return new WindowPreviewCardVisualState(
                "Minimized",
                "Window is minimized.",
                false);
        }

        return isForeground
            ? new WindowPreviewCardVisualState(
                "Active",
                "Window is active.",
                true)
            : new WindowPreviewCardVisualState(
                "Running",
                "Window is running.",
                false);
    }
}

public sealed record WindowPreviewCardVisualState(
    string StatusLabel,
    string HelpText,
    bool UsesAccentStroke);
