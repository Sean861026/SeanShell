using System.Runtime.InteropServices;

namespace SeanShell.Windows;

internal static class HighContrastReader
{
    private const uint GetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x00000001;

    public static bool IsEnabled()
    {
        var highContrast = new NativeHighContrast
        {
            Size = (uint)Marshal.SizeOf<NativeHighContrast>(),
        };
        return SystemParametersInfo(
                GetHighContrast,
                highContrast.Size,
                ref highContrast,
                0) &&
            (highContrast.Flags & HighContrastOn) != 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeHighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref NativeHighContrast value,
        uint update);
}
