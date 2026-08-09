using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SeanShell.Windows;

public sealed class ImmersiveWindowChangeObserver : IDisposable
{
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ObjectIdWindow = 0;
    private static readonly uint[] ObservedEvents =
    [
        0x0003, // EVENT_SYSTEM_FOREGROUND
        0x0016, // EVENT_SYSTEM_MINIMIZESTART
        0x0017, // EVENT_SYSTEM_MINIMIZEEND
        0x8001, // EVENT_OBJECT_DESTROY
        0x8002, // EVENT_OBJECT_SHOW
        0x8003, // EVENT_OBJECT_HIDE
        0x800B, // EVENT_OBJECT_LOCATIONCHANGE
    ];

    private readonly WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private int _disposed;

    public ImmersiveWindowChangeObserver()
    {
        _callback = OnWinEvent;
        foreach (var eventType in ObservedEvents)
        {
            var hook = SetWinEventHook(
                eventType,
                eventType,
                0,
                _callback,
                0,
                0,
                WinEventOutOfContext | WinEventSkipOwnProcess);
            if (hook == 0)
            {
                Dispose();
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Unable to observe immersive window changes.");
            }

            _hooks.Add(hook);
        }
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var hook in _hooks)
        {
            _ = UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            windowHandle == 0 ||
            objectId != ObjectIdWindow)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Managed failures must never cross the native accessibility callback.
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        nint moduleHandle,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
