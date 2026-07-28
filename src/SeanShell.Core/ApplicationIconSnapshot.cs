namespace SeanShell.Core;

public sealed class ApplicationIconSnapshot
{
    private const int MaximumDimension = 256;

    public ApplicationIconSnapshot(
        int width,
        int height,
        ReadOnlySpan<byte> bgraPixels)
    {
        if (width is <= 0 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height is <= 0 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var expectedLength = checked(width * height * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException(
                "BGRA pixel data must contain exactly four bytes per pixel.",
                nameof(bgraPixels));
        }

        Width = width;
        Height = height;
        BgraPixels = bgraPixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> BgraPixels { get; }
}
