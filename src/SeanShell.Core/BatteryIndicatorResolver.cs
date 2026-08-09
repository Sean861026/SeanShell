namespace SeanShell.Core;

public enum BatteryIndicatorKind
{
    Unavailable,
    NoBattery,
    Battery,
    Charging,
}

public enum BatteryIndicatorEmphasis
{
    Normal,
    Charging,
    Caution,
    Critical,
    Unavailable,
}

public readonly record struct BatteryIndicatorState(
    BatteryIndicatorKind Kind,
    int Level,
    BatteryIndicatorEmphasis Emphasis);

public static class BatteryIndicatorResolver
{
    public static BatteryIndicatorState Resolve(SystemStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasBattery)
        {
            return new(
                BatteryIndicatorKind.NoBattery,
                10,
                snapshot.IsPluggedIn is null
                    ? BatteryIndicatorEmphasis.Unavailable
                    : BatteryIndicatorEmphasis.Normal);
        }

        if (snapshot.BatteryPercent is not { } rawPercent)
        {
            return new(
                BatteryIndicatorKind.Unavailable,
                0,
                BatteryIndicatorEmphasis.Unavailable);
        }

        var percent = Math.Clamp(rawPercent, 0, 100);
        var level = Math.Clamp((percent + 9) / 10, 0, 10);
        if (snapshot.IsCharging)
        {
            return new(
                BatteryIndicatorKind.Charging,
                level,
                BatteryIndicatorEmphasis.Charging);
        }

        var onBattery = snapshot.IsPluggedIn == false;
        var emphasis = onBattery && percent <= 15
            ? BatteryIndicatorEmphasis.Critical
            : onBattery && percent <= 30
                ? BatteryIndicatorEmphasis.Caution
                : BatteryIndicatorEmphasis.Normal;
        return new(BatteryIndicatorKind.Battery, level, emphasis);
    }
}
