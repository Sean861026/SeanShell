namespace SeanShell.Core;

public static class WindowPreviewLayout
{
    public const int MaximumVisibleWindows = 6;
    public const int CardWidth = 272;
    public const int CardHeight = 204;
    public const int Gap = 8;
    public const int OuterPadding = 10;

    public static WindowPreviewLayoutResult Calculate(int windowCount)
    {
        if (windowCount <= 0)
        {
            return new WindowPreviewLayoutResult(0, 0, 0, 0, 0);
        }

        var visibleCount = Math.Min(windowCount, MaximumVisibleWindows);
        var columns = visibleCount switch
        {
            1 => 1,
            4 => 2,
            _ => Math.Min(visibleCount, 3),
        };
        var rows = (int)Math.Ceiling(visibleCount / (double)columns);
        var width =
            (OuterPadding * 2) +
            (columns * CardWidth) +
            ((columns - 1) * Gap);
        var height =
            (OuterPadding * 2) +
            (rows * CardHeight) +
            ((rows - 1) * Gap);

        return new WindowPreviewLayoutResult(
            visibleCount,
            columns,
            rows,
            width,
            height);
    }
}

public sealed record WindowPreviewLayoutResult(
    int VisibleCount,
    int Columns,
    int Rows,
    int Width,
    int Height);
