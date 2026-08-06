using System.Runtime.InteropServices;

namespace SeanShell.Windows;

public static class KeyboardModifierStateReader
{
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const short KeyPressedMask = unchecked((short)0x8000);

    public static bool IsShiftPressed() =>
        IsPressed(VirtualKeyShift);

    public static bool IsControlPressed() =>
        IsPressed(VirtualKeyControl);

    private static bool IsPressed(int virtualKey) =>
        (GetKeyState(virtualKey) & KeyPressedMask) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
