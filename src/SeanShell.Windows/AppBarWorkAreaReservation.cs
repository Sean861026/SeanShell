using System.ComponentModel;
using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class AppBarWorkAreaReservation : IDisposable
{
    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbmWindowPosChanged = 0x00000009;
    private const uint AbeBottom = 3;
    private const uint CallbackMessage = 0x8000 + 0x353;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    private nint _windowHandle;
    private bool _registered;

    public WorkAreaReservationResult Reserve(
        nint windowHandle,
        nint monitorHandle,
        int height)
    {
        if (windowHandle == 0)
        {
            return WorkAreaReservationResult.Failed(
                "The Dock window handle is unavailable.");
        }

        if (monitorHandle == 0)
        {
            return WorkAreaReservationResult.Failed(
                "The display handle is unavailable.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        try
        {
            var previous = Release();
            if (!previous.Success)
            {
                return previous;
            }

            var monitor = GetMonitor(monitorHandle);
            var plan = WorkAreaReservationLayout.Calculate(
                ToBounds(monitor.MonitorArea),
                height);

            var registration = CreateData(windowHandle);
            registration.CallbackMessage = CallbackMessage;
            if (SHAppBarMessage(AbmNew, ref registration) == 0)
            {
                return WorkAreaReservationResult.Failed(
                    "Windows rejected the Dock work-area registration.");
            }

            _registered = true;
            _windowHandle = windowHandle;
            var position = CreateData(windowHandle);
            position.Edge = AbeBottom;
            position.Rectangle = monitor.MonitorArea;
            position.Rectangle.Top = Math.Max(
                position.Rectangle.Top,
                position.Rectangle.Bottom - plan.AdditionalHeight);

            _ = SHAppBarMessage(AbmQueryPos, ref position);
            position.Rectangle.Top = Math.Max(
                monitor.MonitorArea.Top,
                position.Rectangle.Bottom - plan.AdditionalHeight);
            _ = SHAppBarMessage(AbmSetPos, ref position);
            if (!SetWindowPos(
                windowHandle,
                0,
                position.Rectangle.Left,
                position.Rectangle.Top,
                position.Rectangle.Right - position.Rectangle.Left,
                position.Rectangle.Bottom - position.Rectangle.Top,
                SwpNoActivate | SwpNoZOrder))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Unable to position the Dock work-area reservation.");
            }

            _ = SHAppBarMessage(AbmWindowPosChanged, ref position);
            return WorkAreaReservationResult.Applied(new DockBounds(
                position.Rectangle.Left,
                position.Rectangle.Top,
                position.Rectangle.Right - position.Rectangle.Left,
                monitor.MonitorArea.Bottom - position.Rectangle.Top));
        }
        catch (Exception exception) when (
            exception is Win32Exception or OverflowException)
        {
            _ = Release();
            return WorkAreaReservationResult.Failed(exception.Message);
        }
    }

    public WorkAreaReservationResult Release()
    {
        if (!_registered)
        {
            return WorkAreaReservationResult.Released();
        }

        var removal = CreateData(_windowHandle);
        var removed = SHAppBarMessage(AbmRemove, ref removal) != 0;
        if (!removed)
        {
            return WorkAreaReservationResult.Failed(
                "Windows did not release the Dock work area.");
        }

        _registered = false;
        _windowHandle = 0;
        return WorkAreaReservationResult.Released();
    }

    public void Dispose() => _ = Release();

    private static AppBarData CreateData(nint windowHandle) =>
        new()
        {
            Size = checked((uint)Marshal.SizeOf<AppBarData>()),
            WindowHandle = windowHandle,
        };

    private static DockBounds ToBounds(NativeRectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

    private static MonitorInfo GetMonitor(nint monitorHandle)
    {
        var monitor = new MonitorInfo
        {
            Size = checked((uint)Marshal.SizeOf<MonitorInfo>()),
        };
        if (!GetMonitorInfo(monitorHandle, ref monitor))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to read the display work area.");
        }

        return monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public nint WindowHandle;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRectangle Rectangle;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern nuint SHAppBarMessage(
        uint message,
        ref AppBarData data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitorHandle,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

public sealed record WorkAreaReservationResult(
    bool Success,
    DockBounds? ReservedArea,
    string? Error)
{
    public static WorkAreaReservationResult Applied(DockBounds area) =>
        new(true, area, null);

    public static WorkAreaReservationResult Released() =>
        new(true, null, null);

    public static WorkAreaReservationResult Failed(string error) =>
        new(false, null, error);
}
