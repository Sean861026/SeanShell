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
    private const int DwmwaExtendedFrameBounds = 9;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const uint WmClose = 0x0010;
    private const uint MonitorDefaultToNearest = 2;
    private readonly object _cacheGate = new();
    private readonly Dictionary<int, ApplicationIconSnapshot?> _iconCache = [];
    private readonly NativeApplicationIconReader _iconReader = new();
    private readonly VirtualDesktopWindowService _virtualDesktops = new();
    private IReadOnlyList<DesktopWindowSnapshot> _cachedWindows = [];
    private long _cacheExpiresAt;
    private long _activationRevision;

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

    public void InvalidateCache()
    {
        lock (_cacheGate)
        {
            _cacheExpiresAt = 0;
        }
    }

    private IReadOnlyList<DesktopWindowSnapshot> CaptureCore()
    {
        var windows = new List<DesktopWindowSnapshot>();
        var shellWindow = GetShellWindow();
        var foregroundWindow = GetForegroundWindow();

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

            if (!_virtualDesktops.IsOnCurrentDesktop(handle))
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
                IsIconic(handle),
                MonitorFromWindow(handle, MonitorDefaultToNearest),
                handle == foregroundWindow,
                GetApplicationIcon(handle, checked((int)processId))));

            return true;
        }, 0);

        var activeProcessIds = windows
            .Select(static window => window.ProcessId)
            .ToHashSet();
        foreach (var cachedProcessId in _iconCache.Keys
                     .Where(processId => !activeProcessIds.Contains(processId))
                     .ToArray())
        {
            _iconCache.Remove(cachedProcessId);
        }

        return windows
            .OrderBy(static window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ApplicationIconSnapshot? GetApplicationIcon(
        nint windowHandle,
        int processId)
    {
        if (_iconCache.TryGetValue(processId, out var icon))
        {
            return icon;
        }

        if (_iconCache.Count >= 128)
        {
            _iconCache.Clear();
        }

        try
        {
            icon = _iconReader.ReadWindowIcon(windowHandle, processId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine(
                $"Unable to read the application icon for process {processId}. {exception}");
            icon = null;
        }

        _iconCache[processId] = icon;
        return icon;
    }

    public Task<bool> ToggleTaskbarWindowAsync(nint handle, CancellationToken cancellationToken = default)
    {
        if (!IsWindow(handle))
        {
            return Task.FromResult(false);
        }

        var action = TaskbarWindowActionResolver.Resolve(
            handle == GetForegroundWindow(),
            IsIconic(handle));
        if (action == TaskbarWindowAction.Minimize)
        {
            return MinimizeAsync(handle, cancellationToken);
        }

        return RestoreAndActivateAsync(handle, cancellationToken);
    }

    public Task<bool> MinimizeAsync(nint handle, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activationRevision);
        return SetMinimizedAsync(handle, minimized: true, cancellationToken);
    }

    public Task<bool> RestoreAsync(nint handle, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activationRevision);
        return SetMinimizedAsync(handle, minimized: false, cancellationToken);
    }

    public nint CaptureForegroundWindowHandle() => GetForegroundWindow();

    public bool IsForegroundApplication(nint handle) =>
        IsWindow(handle) && !IsIconic(handle) && GetForegroundWindow() == handle;

    public nint CaptureForegroundMonitorHandle()
    {
        var foregroundWindow = GetForegroundWindow();
        return foregroundWindow == 0
            ? 0
            : MonitorFromWindow(foregroundWindow, MonitorDefaultToNearest);
    }

    public IReadOnlyList<nint> CaptureImmersiveMonitorHandles(bool includeMaximized = true)
    {
        var monitorHandles = new HashSet<nint>();
        var shellWindow = GetShellWindow();
        EnumWindows((handle, _) =>
        {
            if (handle == shellWindow ||
                IsIconic(handle) ||
                !IsTaskbarWindow(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || processId == Environment.ProcessId)
            {
                return true;
            }

            var monitorHandle = MonitorFromWindow(
                handle,
                MonitorDefaultToNearest);
            if (monitorHandle != 0 &&
                TryGetWindowBounds(handle, out var windowBounds) &&
                TryGetMonitorBounds(monitorHandle, out var monitorBounds) &&
                WindowImmersiveStateResolver.IsImmersive(
                    includeMaximized && IsZoomed(handle),
                    windowBounds,
                    monitorBounds))
            {
                monitorHandles.Add(monitorHandle);
            }

            return true;
        }, 0);
        return monitorHandles.ToArray();
    }

    public IReadOnlyList<nint> CaptureTopApplicationFullscreenMonitorHandles()
    {
        var windowsInZOrder = new List<ActivityIslandWindowCoverage>();
        var shellWindow = GetShellWindow();
        EnumWindows((handle, _) =>
        {
            if (handle == shellWindow || IsIconic(handle) || !IsTaskbarWindow(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || processId == Environment.ProcessId)
            {
                return true;
            }

            var monitorHandle = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitorHandle != 0)
            {
                var coversMonitor = TryGetWindowBounds(handle, out var windowBounds) &&
                    TryGetMonitorBounds(monitorHandle, out var monitorBounds) &&
                    WindowImmersiveStateResolver.IsImmersive(false, windowBounds, monitorBounds);
                windowsInZOrder.Add(new ActivityIslandWindowCoverage(monitorHandle, coversMonitor));
            }

            return true;
        }, 0);

        // Island suppression follows the top eligible app on each display,
        // unlike Dock work-area policy, which intentionally considers all apps.
        return ActivityIslandFullscreenResolver.Resolve(windowsInZOrder);
    }

    public async Task<bool> RestoreAndActivateAsync(
        nint handle,
        CancellationToken cancellationToken = default)
    {
        var revision = Interlocked.Increment(ref _activationRevision);
        var previousForeground = GetForegroundWindow();
        if (!await SetMinimizedAsync(handle, minimized: false, cancellationToken)
                .ConfigureAwait(true) ||
            cancellationToken.IsCancellationRequested ||
            revision != Interlocked.Read(ref _activationRevision))
        {
            return false;
        }

        // Do not issue an additional foreground request after the user selects
        // another app. This cannot cancel SW_RESTORE already posted to Windows:
        // that native restore itself activates and may execute after a timeout.
        // SW_SHOWNOACTIVATE is not an equivalent restore-to-maximized operation.
        var foreground = GetForegroundWindow();
        if (foreground != previousForeground && foreground != handle)
        {
            return false;
        }

        if (!SetForegroundWindow(handle))
        {
            return false;
        }

        return await WaitForWindowStateAsync(
            handle,
            () => GetForegroundWindow() == handle && !IsIconic(handle),
            () => revision == Interlocked.Read(ref _activationRevision),
            cancellationToken).ConfigureAwait(true);
    }

    private static async Task<bool> SetMinimizedAsync(
        nint handle,
        bool minimized,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !IsWindow(handle))
        {
            return false;
        }

        if (IsIconic(handle) == minimized)
        {
            return true;
        }

        // This posts to the foreign window's thread instead of synchronously
        // waiting for a potentially hung application to process WM_SHOWWINDOW.
        if (!ShowWindowAsync(handle, minimized ? SwMinimize : SwRestore))
        {
            return false;
        }

        return await WaitForWindowStateAsync(
            handle,
            () => IsIconic(handle) == minimized,
            static () => true,
            cancellationToken).ConfigureAwait(true);
    }

    private static async Task<bool> WaitForWindowStateAsync(
        nint handle,
        Func<bool> matches,
        Func<bool> isCurrent,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (!cancellationToken.IsCancellationRequested && IsWindow(handle) && isCurrent())
        {
            if (matches())
            {
                return true;
            }

            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(750))
            {
                break;
            }

            try
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        return false;
    }

    public bool RequestClose(nint handle) =>
        IsWindow(handle) && PostMessage(handle, WmClose, 0, 0);

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

    private static bool TryGetWindowBounds(nint handle, out DockBounds bounds)
    {
        var result = DwmGetWindowAttribute(
            handle,
            DwmwaExtendedFrameBounds,
            out NativeRectangle rectangle,
            Marshal.SizeOf<NativeRectangle>()) == 0 ||
            GetWindowRect(handle, out rectangle);
        bounds = ToBounds(rectangle);
        return result;
    }

    private static bool TryGetMonitorBounds(
        nint monitorHandle,
        out DockBounds bounds)
    {
        var monitor = new MonitorInfo
        {
            Size = checked((uint)Marshal.SizeOf<MonitorInfo>()),
        };
        var result = GetMonitorInfo(monitorHandle, ref monitor);
        bounds = ToBounds(monitor.MonitorArea);
        return result;
    }

    private static DockBounds ToBounds(NativeRectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

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
    private static extern bool IsZoomed(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        nint handle,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint handle,
        out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitorHandle,
        ref MonitorInfo monitorInfo);

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out NativeRectangle value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }
}
