namespace SeanShell.Core;

public static class WindowPreviewAspectFit
{
    public static WindowPreviewRectangle Fit(
        int sourceWidth,
        int sourceHeight,
        WindowPreviewRectangle target)
    {
        if (sourceWidth <= 0 ||
            sourceHeight <= 0 ||
            target.Width <= 0 ||
            target.Height <= 0)
        {
            return target;
        }

        var scale = Math.Min(
            target.Width / (double)sourceWidth,
            target.Height / (double)sourceHeight);
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new WindowPreviewRectangle(
            target.X + ((target.Width - width) / 2),
            target.Y + ((target.Height - height) / 2),
            width,
            height);
    }
}

public sealed record WindowPreviewRectangle(
    int X,
    int Y,
    int Width,
    int Height);
