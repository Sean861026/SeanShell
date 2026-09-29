using System.Globalization;
using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class NativeInputLayoutBackend : IInputLayoutBackend
{
    private const uint WmInputLanguageChangeRequest = 0x0050;
    private const uint RootAncestor = 2;

    public InputWindowIdentity? CaptureTarget(nint handle)
    {
        if (handle == 0 || !IsWindow(handle))
        {
            return null;
        }

        var threadId = GetWindowThreadProcessId(handle, out var processId);
        return threadId == 0 || processId == 0
            ? null
            : new InputWindowIdentity(handle, threadId, processId);
    }

    public IReadOnlyList<InputLayoutOption> CaptureLayouts()
    {
        var handles = new nint[64];
        var count = GetKeyboardLayoutList(handles.Length, handles);
        return handles.Take(Math.Clamp(count, 0, handles.Length))
            .Where(static handle => handle != 0)
            .Distinct()
            .Select(static handle => new InputLayoutOption(handle, Describe(handle)))
            .ToArray();
    }

    public nint ReadActiveLayout(InputWindowIdentity target) =>
        CaptureTarget(target.Handle) == target ? GetKeyboardLayout(target.ThreadId) : 0;

    public bool IsForeground(InputWindowIdentity target) =>
        GetForegroundWindow() == target.Handle && CaptureTarget(target.Handle) == target;

    public bool TryPostChange(InputWindowIdentity target, nint layout)
    {
        if (!IsForeground(target) || target.ProcessId == (uint)Environment.ProcessId ||
            layout == 0 || !CaptureLayouts().Any(option => option.Handle == layout))
        {
            return false;
        }

        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(target.ThreadId, ref info) || info.Focus == 0 ||
            GetAncestor(info.Focus, RootAncestor) != target.Handle)
        {
            return false;
        }

        var focusThread = GetWindowThreadProcessId(info.Focus, out var focusProcess);
        if (focusThread != target.ThreadId || focusProcess != target.ProcessId ||
            !IsForeground(target))
        {
            return false;
        }

        // Asynchronous, focused-window-only request. No broadcast, SendInput,
        // AttachThreadInput, keyboard hooks, or activation of our own thread's layout.
        // UIPI and the receiving application remain free to reject the request.
        return PostMessage(info.Focus, WmInputLanguageChangeRequest, 0, layout);
    }

    private static string Describe(nint handle)
    {
        var identifier = unchecked((uint)handle.ToInt64());
        string name;
        try
        {
            name = CultureInfo.GetCultureInfo((int)(identifier & 0xffff)).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            // Transient LANGIDs are not stable locale names; keep the runtime handle.
            name = "Input language";
        }

        return $"{name} · {identifier:X8}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetKeyboardLayoutList(int count, [Out] nint[] layouts);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint handle, uint message, nuint wParam, nint lParam);
}
