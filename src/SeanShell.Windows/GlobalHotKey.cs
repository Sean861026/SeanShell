using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SeanShell.Windows;

[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000,
}

public sealed class GlobalHotKey : IDisposable
{
    private const uint WmHotKey = 0x0312;
    private const int HotKeyId = 0x5348;
    private static readonly nuint SubclassId = 0x5348;

    private readonly nint _windowHandle;
    private readonly SubclassProc _windowProc;
    private bool _disposed;

    public GlobalHotKey(nint windowHandle, HotKeyModifiers modifiers, uint virtualKey)
    {
        _windowHandle = windowHandle;
        _windowProc = WindowProc;

        if (!SetWindowSubclass(_windowHandle, _windowProc, SubclassId, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to observe window messages.");
        }

        if (!RegisterHotKey(_windowHandle, HotKeyId, (uint)modifiers, virtualKey))
        {
            RemoveWindowSubclass(_windowHandle, _windowProc, SubclassId);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to register the launcher shortcut.");
        }
    }

    public event EventHandler? Pressed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterHotKey(_windowHandle, HotKeyId);
        RemoveWindowSubclass(_windowHandle, _windowProc, SubclassId);
        _disposed = true;
    }

    private nint WindowProc(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WmHotKey && (int)wParam == HotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private delegate nint SubclassProc(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProc callback,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProc callback,
        nuint subclassId);
}
