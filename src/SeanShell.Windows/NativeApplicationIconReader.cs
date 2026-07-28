using System.Runtime.InteropServices;
using System.Text;
using SeanShell.Core;

namespace SeanShell.Windows;

internal sealed class NativeApplicationIconReader
{
    private const int DefaultIconSize = 32;
    private const int GclpHicon = -14;
    private const int GclpHiconSmall = -34;
    private const uint IconSmall = 0;
    private const uint IconBig = 1;
    private const uint IconSmall2 = 2;
    private const uint WmGetIcon = 0x007F;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const uint DiNormal = 0x0003;

    public ApplicationIconSnapshot? ReadWindowIcon(nint windowHandle, int processId)
    {
        var iconHandle = GetWindowIcon(windowHandle);
        if (iconHandle != 0)
        {
            return Render(iconHandle);
        }

        var executablePath = GetProcessImagePath(processId);
        return executablePath is null ? null : ReadFileIcon(executablePath);
    }

    public ApplicationIconSnapshot? ReadFileIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var result = SHGetFileInfo(
            path,
            0,
            out var fileInfo,
            checked((uint)Marshal.SizeOf<ShFileInfo>()),
            ShgfiIcon | ShgfiLargeIcon);
        if (result == 0 || fileInfo.IconHandle == 0)
        {
            return null;
        }

        try
        {
            return Render(fileInfo.IconHandle);
        }
        finally
        {
            _ = DestroyIcon(fileInfo.IconHandle);
        }
    }

    private static nint GetWindowIcon(nint windowHandle)
    {
        foreach (var iconType in new[] { IconBig, IconSmall2, IconSmall })
        {
            if (SendMessageTimeout(
                    windowHandle,
                    WmGetIcon,
                    checked((nint)iconType),
                    0,
                    SmtoAbortIfHung,
                    75,
                    out var iconHandle) &&
                iconHandle != 0)
            {
                return iconHandle;
            }
        }

        var classIcon = GetClassLongPointer(windowHandle, GclpHicon);
        return classIcon != 0
            ? classIcon
            : GetClassLongPointer(windowHandle, GclpHiconSmall);
    }

    private static ApplicationIconSnapshot? Render(nint iconHandle)
    {
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                Width = DefaultIconSize,
                Height = -DefaultIconSize,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
            },
        };
        var bitmap = CreateDIBSection(
            0,
            ref info,
            DibRgbColors,
            out var pixels,
            0,
            0);
        if (bitmap == 0 || pixels == 0)
        {
            return null;
        }

        var deviceContext = CreateCompatibleDC(0);
        if (deviceContext == 0)
        {
            _ = DeleteObject(bitmap);
            return null;
        }

        var previousObject = SelectObject(deviceContext, bitmap);
        try
        {
            if (!DrawIconEx(
                    deviceContext,
                    0,
                    0,
                    iconHandle,
                    DefaultIconSize,
                    DefaultIconSize,
                    0,
                    0,
                    DiNormal))
            {
                return null;
            }

            var buffer = new byte[DefaultIconSize * DefaultIconSize * 4];
            Marshal.Copy(pixels, buffer, 0, buffer.Length);
            RepairLegacyAlpha(buffer);
            return new ApplicationIconSnapshot(
                DefaultIconSize,
                DefaultIconSize,
                buffer);
        }
        finally
        {
            if (previousObject != 0)
            {
                _ = SelectObject(deviceContext, previousObject);
            }

            _ = DeleteDC(deviceContext);
            _ = DeleteObject(bitmap);
        }
    }

    private static void RepairLegacyAlpha(byte[] pixels)
    {
        var hasAlpha = false;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        if (hasAlpha)
        {
            return;
        }

        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0 ||
                pixels[index + 1] != 0 ||
                pixels[index + 2] != 0)
            {
                pixels[index + 3] = byte.MaxValue;
            }
        }
    }

    private static string? GetProcessImagePath(int processId)
    {
        var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            false,
            checked((uint)processId));
        if (processHandle == 0)
        {
            return null;
        }

        try
        {
            var capacity = 1024;
            var path = new StringBuilder(capacity);
            return QueryFullProcessImageName(processHandle, 0, path, ref capacity)
                ? path.ToString()
                : null;
        }
        finally
        {
            _ = CloseHandle(processHandle);
        }
    }

    private static nint GetClassLongPointer(nint windowHandle, int index) =>
        nint.Size == 8
            ? GetClassLongPtr64(windowHandle, index)
            : new nint(unchecked((int)GetClassLong32(windowHandle, index)));

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint RedMask;
        public uint GreenMask;
        public uint BlueMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendMessageTimeout(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nint result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLong32(nint windowHandle, int index);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint pixels,
        nint section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(
        nint deviceContext,
        int x,
        int y,
        nint iconHandle,
        int width,
        int height,
        uint animationStep,
        nint flickerFreeBrush,
        uint flags);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        nint processHandle,
        uint flags,
        StringBuilder executableName,
        ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
