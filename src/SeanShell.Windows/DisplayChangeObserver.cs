using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SeanShell.Windows;

public sealed class DisplayChangeObserver : IDisposable
{
    private const uint WmDisplayChange = 0x007E;
    private static readonly nuint SubclassId = 0x5344;

    private readonly nint _windowHandle;
    private readonly SubclassProc _windowProc;
    private bool _disposed;

    public DisplayChangeObserver(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _windowProc = WindowProc;
        if (!SetWindowSubclass(_windowHandle, _windowProc, SubclassId, 0))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to observe display changes.");
        }
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

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
        if (message == WmDisplayChange)
        {
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // Never allow a managed observer failure to cross the native callback.
            }
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

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProc callback,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProc callback,
        nuint subclassId);
}
