using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class LayeredDockIconWindow : IDisposable
{
    private const int DesignWidth = 96;
    private const int DesignHeight = 112;
    private const int DesignIconSize = 76;
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcAlpha = 0x01;
    private const byte AcSrcOver = 0x00;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint WsPopup = 0x80000000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;
    private static readonly object ClassGate = new();
    private static readonly WindowProcedure WindowCallback = DefWindowProc;
    private static readonly string WindowClassName =
        $"SeanShell.LayeredDockIcon.{Environment.ProcessId}";
    private static bool _classRegistered;
    private nint _window;
    private ApplicationIconSnapshot? _snapshot;
    private DockMagnifierBounds? _bounds;

    public LayeredDockIconWindow()
    {
        EnsureWindowClass();
        _window = CreateWindowEx(
            WsExLayered |
            WsExTransparent |
            WsExToolWindow |
            WsExNoActivate |
            WsExTopmost,
            WindowClassName,
            "Dock icon magnifier",
            WsPopup,
            0,
            0,
            0,
            0,
            0,
            0,
            GetModuleHandle(null),
            0);
        if (_window == 0)
        {
            throw new InvalidOperationException(
                $"Unable to create the Dock icon overlay. Win32 error: {Marshal.GetLastWin32Error()}.");
        }
    }

    public bool IsVisible { get; private set; }

    public bool Show(
        ApplicationIconSnapshot snapshot,
        int anchorCenterX,
        int anchorBottomY,
        DisplayMonitorSnapshot monitor,
        double scaleFactor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _bounds = DockMagnifierPlacement.Calculate(
            anchorCenterX,
            anchorBottomY,
            monitor,
            DesignWidth,
            DesignHeight,
            scaleFactor);
        if (!Render(scaleFactor, pressed: false))
        {
            _snapshot = null;
            _bounds = null;
            return false;
        }

        _ = ShowWindow(_window, SwShowNoActivate);
        IsVisible = true;
        return true;
    }

    public void SetPressed(bool pressed, double scaleFactor)
    {
        if (!IsVisible || _snapshot is null || _bounds is null)
        {
            return;
        }

        _ = Render(scaleFactor, pressed);
    }

    public void Dismiss()
    {
        if (!IsVisible)
        {
            return;
        }

        _ = ShowWindow(_window, SwHide);
        IsVisible = false;
        _snapshot = null;
        _bounds = null;
    }

    public void Dispose()
    {
        if (_window == 0)
        {
            return;
        }

        _ = DestroyWindow(_window);
        _window = 0;
        IsVisible = false;
        GC.SuppressFinalize(this);
    }

    private bool Render(double scaleFactor, bool pressed)
    {
        var snapshot = _snapshot ?? throw new InvalidOperationException();
        var bounds = _bounds ?? throw new InvalidOperationException();
        var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
        var preferredIconSize = DisplayScaleLayout.ToPhysicalPixels(
            DesignIconSize,
            scaleFactor);
        var iconSize = Math.Min(
            pressed
                ? (int)Math.Round(preferredIconSize * 0.92)
                : preferredIconSize,
            Math.Min(bounds.Width, bounds.Height));
        var iconX = (bounds.Width - iconSize) / 2;
        var iconY = Math.Max(0, DisplayScaleLayout.ToPhysicalPixels(5, scaleFactor));
        CompositeScaledIcon(snapshot, pixels, bounds.Width, iconX, iconY, iconSize);

        var screen = GetDC(0);
        if (screen == 0)
        {
            return false;
        }

        var memory = CreateCompatibleDC(screen);
        if (memory == 0)
        {
            _ = ReleaseDC(0, screen);
            return false;
        }

        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bounds.Width,
                Height = -bounds.Height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
            },
        };
        var bitmap = CreateDIBSection(
            memory,
            ref bitmapInfo,
            DibRgbColors,
            out var destination,
            0,
            0);
        if (bitmap == 0 || destination == 0)
        {
            if (bitmap != 0)
            {
                _ = DeleteObject(bitmap);
            }

            _ = DeleteDC(memory);
            _ = ReleaseDC(0, screen);
            return false;
        }

        var previous = SelectObject(memory, bitmap);
        var updated = false;
        try
        {
            Marshal.Copy(pixels, 0, destination, pixels.Length);
            var target = new NativePoint(bounds.X, bounds.Y);
            var size = new NativeSize(bounds.Width, bounds.Height);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha,
            };
            updated = UpdateLayeredWindow(
                _window,
                screen,
                ref target,
                ref size,
                memory,
                ref source,
                0,
                ref blend,
                UlwAlpha);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }

        return updated;
    }

    private static void CompositeScaledIcon(
        ApplicationIconSnapshot snapshot,
        byte[] destination,
        int destinationWidth,
        int offsetX,
        int offsetY,
        int targetSize)
    {
        var source = snapshot.BgraPixels.Span;
        for (var targetY = 0; targetY < targetSize; targetY++)
        {
            var sourceY = (((targetY + 0.5) * snapshot.Height) / targetSize) - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, snapshot.Height - 1);
            var y1 = Math.Min(y0 + 1, snapshot.Height - 1);
            var yWeight = sourceY - Math.Floor(sourceY);
            for (var targetX = 0; targetX < targetSize; targetX++)
            {
                var sourceX = (((targetX + 0.5) * snapshot.Width) / targetSize) - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, snapshot.Width - 1);
                var x1 = Math.Min(x0 + 1, snapshot.Width - 1);
                var xWeight = sourceX - Math.Floor(sourceX);
                var destinationIndex = (((offsetY + targetY) * destinationWidth) + offsetX + targetX) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var top = Lerp(
                        source[((y0 * snapshot.Width) + x0) * 4 + channel],
                        source[((y0 * snapshot.Width) + x1) * 4 + channel],
                        xWeight);
                    var bottom = Lerp(
                        source[((y1 * snapshot.Width) + x0) * 4 + channel],
                        source[((y1 * snapshot.Width) + x1) * 4 + channel],
                        xWeight);
                    destination[destinationIndex + channel] =
                        (byte)Math.Clamp((int)Math.Round(Lerp(top, bottom, yWeight)), 0, 255);
                }
            }
        }
    }

    private static double Lerp(double first, double second, double amount) =>
        first + ((second - first) * amount);

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProcedure = WindowCallback,
                Instance = GetModuleHandle(null),
                ClassName = WindowClassName,
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException(
                    $"Unable to register the Dock icon overlay. Win32 error: {Marshal.GetLastWin32Error()}.");
            }

            _classRegistered = true;
        }
    }

    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint RedMask;
        public uint GreenMask;
        public uint BlueMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint pixels,
        nint section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint graphicObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint graphicObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        nint window,
        nint destinationDeviceContext,
        ref NativePoint destination,
        ref NativeSize size,
        nint sourceDeviceContext,
        ref NativePoint source,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);
}
