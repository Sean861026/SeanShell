using System.ComponentModel;
using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class DisplayMonitorService
{
    private const uint MonitorInfoPrimary = 0x00000001;

    public IReadOnlyList<DisplayMonitorSnapshot> Capture()
    {
        var monitors = new List<DisplayMonitorSnapshot>();
        if (!EnumDisplayMonitors(0, 0, (handle, _, _, _) =>
        {
            var info = new MonitorInfoEx();
            if (!GetMonitorInfo(handle, ref info))
            {
                return true;
            }

            monitors.Add(new DisplayMonitorSnapshot(
                handle,
                info.DeviceName,
                info.WorkArea.Left,
                info.WorkArea.Top,
                info.WorkArea.Right - info.WorkArea.Left,
                info.WorkArea.Bottom - info.WorkArea.Top,
                (info.Flags & MonitorInfoPrimary) != 0));
            return true;
        }, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate displays.");
        }

        return monitors
            .OrderByDescending(static monitor => monitor.IsPrimary)
            .ThenBy(static monitor => monitor.WorkAreaX)
            .ThenBy(static monitor => monitor.WorkAreaY)
            .ToArray();
    }

    private delegate bool MonitorEnumProc(
        nint monitorHandle,
        nint deviceContext,
        nint monitorRectangle,
        nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public MonitorInfoEx()
        {
            Size = checked((uint)Marshal.SizeOf<MonitorInfoEx>());
            DeviceName = string.Empty;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRectangle,
        MonitorEnumProc callback,
        nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitorHandle, ref MonitorInfoEx monitorInfo);
}
