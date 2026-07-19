using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class DesktopWindowService
{
    private const int CacheDurationMilliseconds = 1_000;
    private const int GwlExStyle = -20;
    private const uint GwOwner = 4;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmwaCloaked = 14;
    private const int SwRestore = 9;
    private readonly object _cacheGate = new();
    private IReadOnlyList<DesktopWindowSnapshot> _cachedWindows = [];
    private long _cacheExpiresAt;

    public IReadOnlyList<DesktopWindowSnapshot> Capture()
    {
        lock (_cacheGate)
        {
            var now = Environment.TickCount64;
            if (now < _cacheExpiresAt)
            {
                return _cachedWindows;
            }

            _cachedWindows = CaptureCore();
            _cacheExpiresAt = now + CacheDurationMilliseconds;
            return _cachedWindows;
        }
    }

    private static IReadOnlyList<DesktopWindowSnapshot> CaptureCore()
    {
        var windows = new List<DesktopWindowSnapshot>();
        var shellWindow = GetShellWindow();

        EnumWindows((handle, _) =>
        {
            if (handle == shellWindow || !IsTaskbarWindow(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || processId == Environment.ProcessId)
            {
                return true;
            }

            var title = GetTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            windows.Add(new DesktopWindowSnapshot(
                handle,
                checked((int)processId),
                GetProcessName(processId),
                title,
                IsIconic(handle)));

            return true;
        }, 0);

        return windows
            .OrderBy(static window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool Activate(nint handle)
    {
        if (!IsWindow(handle))
        {
            return false;
        }

        if (IsIconic(handle))
        {
            ShowWindow(handle, SwRestore);
        }

        return SetForegroundWindow(handle);
    }

    private static bool IsTaskbarWindow(nint handle)
    {
        if (!IsWindowVisible(handle) || IsCloaked(handle))
        {
            return false;
        }

        var extendedStyle = GetExtendedStyle(handle);
        if ((extendedStyle & WsExToolWindow) != 0)
        {
            return false;
        }

        var owner = GetWindow(handle, GwOwner);
        return owner == 0 || (extendedStyle & WsExAppWindow) != 0;
    }

    private static bool IsCloaked(nint handle)
    {
        var cloaked = 0;
        return DwmGetWindowAttribute(
            handle,
            DwmwaCloaked,
            out cloaked,
            Marshal.SizeOf<int>()) == 0 && cloaked != 0;
    }

    private static string GetTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static long GetExtendedStyle(nint handle) =>
        nint.Size == 8
            ? GetWindowLongPtr64(handle, GwlExStyle).ToInt64()
            : GetWindowLong32(handle, GwlExStyle);

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch
        {
            return "Application";
        }
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint handle, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out int value,
        int valueSize);
}
