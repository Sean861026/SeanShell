namespace SeanShell.Core;

public static class WindowPreviewFallbackPresentation
{
    public static WindowPreviewFallbackState Resolve(
        bool thumbnailAvailable,
        bool retryScheduled)
    {
        if (thumbnailAvailable)
        {
            return WindowPreviewFallbackState.Hidden;
        }

        return retryScheduled
            ? WindowPreviewFallbackState.Loading
            : WindowPreviewFallbackState.Unavailable;
    }
}

public enum WindowPreviewFallbackState
{
    Hidden,
    Loading,
    Unavailable,
}
