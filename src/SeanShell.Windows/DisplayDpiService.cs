using System.Runtime.InteropServices;

namespace SeanShell.Windows;

public static class DisplayDpiService
{
    private const uint DefaultDpi = 96;
    private const int MonitorDpiTypeEffective = 0;

    public static double GetScaleFactor(nint monitorHandle)
    {
        if (monitorHandle == 0)
        {
            return 1;
        }

        try
        {
            var result = GetDpiForMonitor(
                monitorHandle,
                MonitorDpiTypeEffective,
                out var dpiX,
                out _);
            return result == 0 && dpiX > 0
                ? dpiX / (double)DefaultDpi
                : 1;
        }
        catch (DllNotFoundException)
        {
            return 1;
        }
        catch (EntryPointNotFoundException)
        {
            return 1;
        }
    }

    public static double GetWindowScaleFactor(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return 1;
        }

        var dpi = GetDpiForWindow(windowHandle);
        return dpi > 0
            ? dpi / (double)DefaultDpi
            : 1;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        nint monitorHandle,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
