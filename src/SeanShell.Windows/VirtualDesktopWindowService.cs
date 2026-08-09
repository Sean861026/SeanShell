using System.Runtime.InteropServices;

namespace SeanShell.Windows;

public sealed class VirtualDesktopWindowService
{
    private readonly IVirtualDesktopManager? _manager = TryCreateManager();

    public bool IsOnCurrentDesktop(nint windowHandle)
    {
        if (windowHandle == 0 || _manager is null)
        {
            return true;
        }

        try
        {
            var result = _manager.IsWindowOnCurrentVirtualDesktop(
                windowHandle,
                out var isOnCurrentDesktop);
            return result < 0 || isOnCurrentDesktop;
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException)
        {
            return true;
        }
    }

    private static IVirtualDesktopManager? TryCreateManager()
    {
        try
        {
            return (IVirtualDesktopManager)new VirtualDesktopManager();
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException)
        {
            return null;
        }
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(
            nint topLevelWindow,
            [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(nint topLevelWindow, in Guid desktopId);
    }

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private sealed class VirtualDesktopManager : IVirtualDesktopManager
    {
        public extern int IsWindowOnCurrentVirtualDesktop(
            nint topLevelWindow,
            out bool onCurrentDesktop);

        public extern int GetWindowDesktopId(
            nint topLevelWindow,
            out Guid desktopId);

        public extern int MoveWindowToDesktop(
            nint topLevelWindow,
            in Guid desktopId);
    }
}
