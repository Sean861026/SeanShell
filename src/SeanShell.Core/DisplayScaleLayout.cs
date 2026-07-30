namespace SeanShell.Core;

public static class DisplayScaleLayout
{
    public static int ToPhysicalPixels(int deviceIndependentPixels, double scaleFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceIndependentPixels);
        ValidateScaleFactor(scaleFactor);

        return checked((int)Math.Ceiling(deviceIndependentPixels * scaleFactor));
    }

    public static int ToDeviceIndependentPixels(int physicalPixels, double scaleFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(physicalPixels);
        ValidateScaleFactor(scaleFactor);

        return Math.Max(1, checked((int)Math.Floor(physicalPixels / scaleFactor)));
    }

    private static void ValidateScaleFactor(double scaleFactor)
    {
        if (!double.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }
    }
}
