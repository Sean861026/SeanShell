namespace SeanShell.Core;

public sealed record SystemStatusSnapshot(
    bool? NetworkAvailable,
    bool HasBattery,
    int? BatteryPercent,
    bool? IsPluggedIn,
    bool IsCharging);

public sealed record SystemStatusDisplayText(
    string Network,
    string Power,
    string AccessibleSummary);

public static class SystemStatusTextFormatter
{
    public static SystemStatusDisplayText Format(SystemStatusSnapshot snapshot)
    {
        var networkState = snapshot.NetworkAvailable switch
        {
            true => "Connected",
            false => "Disconnected",
            null => "Status unavailable",
        };
        var network = $"Network & internet — {networkState}";

        var powerState = FormatPowerState(snapshot);
        var power = $"Power & battery — {powerState}";
        return new SystemStatusDisplayText(
            network,
            power,
            $"Quick settings. Network {networkState.ToLowerInvariant()}. Battery and power {powerState.ToLowerInvariant()}.");
    }

    private static string FormatPowerState(SystemStatusSnapshot snapshot)
    {
        if (!snapshot.HasBattery)
        {
            return snapshot.IsPluggedIn switch
            {
                true => "Plugged in",
                false => "No battery detected",
                null => "Status unavailable",
            };
        }

        var charge = snapshot.BatteryPercent is { } percent
            ? $"{Math.Clamp(percent, 0, 100)}%"
            : "Battery level unavailable";
        if (snapshot.IsCharging)
        {
            return $"{charge} · Charging";
        }

        return snapshot.IsPluggedIn switch
        {
            true => $"{charge} · Plugged in",
            false => $"{charge} · On battery",
            null => charge,
        };
    }
}
