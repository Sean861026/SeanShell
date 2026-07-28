namespace SeanShell.Core;

public static class WorkAreaReservationLayout
{
    public static WorkAreaReservationPlan Calculate(
        DockBounds monitorArea,
        int desiredHeight)
    {
        ArgumentNullException.ThrowIfNull(monitorArea);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desiredHeight);

        var monitorBottom = monitorArea.Y + monitorArea.Height;
        var reservedHeight = Math.Min(desiredHeight, monitorArea.Height);
        return new WorkAreaReservationPlan(
            reservedHeight,
            new DockBounds(
                monitorArea.X,
                monitorBottom - reservedHeight,
                monitorArea.Width,
                reservedHeight));
    }
}

public sealed record WorkAreaReservationPlan(
    int AdditionalHeight,
    DockBounds ReservedArea);
