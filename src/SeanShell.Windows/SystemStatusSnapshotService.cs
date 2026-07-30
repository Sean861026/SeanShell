using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class SystemStatusSnapshotService
{
    private const byte UnknownValue = byte.MaxValue;
    private const byte NoSystemBattery = 128;
    private const byte Charging = 8;

    public SystemStatusSnapshot Capture()
    {
        bool? networkAvailable;
        try
        {
            networkAvailable = NetworkInterface.GetIsNetworkAvailable();
        }
        catch (NetworkInformationException)
        {
            networkAvailable = null;
        }

        if (GetSystemPowerStatus(out var status) == 0)
        {
            return new SystemStatusSnapshot(
                networkAvailable,
                HasBattery: false,
                BatteryPercent: null,
                IsPluggedIn: null,
                IsCharging: false);
        }

        var hasBattery =
            status.BatteryFlag != UnknownValue &&
            (status.BatteryFlag & NoSystemBattery) == 0;
        return new SystemStatusSnapshot(
            networkAvailable,
            hasBattery,
            hasBattery && status.BatteryLifePercent != UnknownValue
                ? status.BatteryLifePercent
                : null,
            status.ACLineStatus switch
            {
                0 => false,
                1 => true,
                _ => null,
            },
            hasBattery && (status.BatteryFlag & Charging) != 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetSystemPowerStatus(
        out NativeSystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
