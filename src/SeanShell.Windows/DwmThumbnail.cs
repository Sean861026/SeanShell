using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class DwmThumbnail : IDisposable
{
    private const uint DestinationRectangle = 0x00000001;
    private const uint Opacity = 0x00000004;
    private const uint Visible = 0x00000008;
    private const uint SourceClientAreaOnly = 0x00000010;
    private nint _handle;

    private DwmThumbnail(nint handle)
    {
        _handle = handle;
    }

    public static bool TryCreate(
        nint destinationWindow,
        nint sourceWindow,
        out DwmThumbnail? thumbnail)
    {
        thumbnail = null;
        if (destinationWindow == 0 || sourceWindow == 0)
        {
            return false;
        }

        var result = DwmRegisterThumbnail(
            destinationWindow,
            sourceWindow,
            out var handle);
        if (result < 0 || handle == 0)
        {
            return false;
        }

        thumbnail = new DwmThumbnail(handle);
        return true;
    }

    public bool TryShow(WindowPreviewRectangle destination, byte opacity = 255)
    {
        if (_handle == 0 ||
            destination.Width <= 0 ||
            destination.Height <= 0)
        {
            return false;
        }

        if (DwmQueryThumbnailSourceSize(_handle, out var sourceSize) >= 0)
        {
            destination = WindowPreviewAspectFit.Fit(
                sourceSize.Width,
                sourceSize.Height,
                destination);
        }

        var properties = new DwmThumbnailProperties
        {
            Flags =
                DestinationRectangle |
                Opacity |
                Visible |
                SourceClientAreaOnly,
            Destination = new NativeRectangle
            {
                Left = destination.X,
                Top = destination.Y,
                Right = destination.X + destination.Width,
                Bottom = destination.Y + destination.Height,
            },
            Opacity = opacity,
            IsVisible = true,
            SourceClientAreaOnly = false,
        };
        return DwmUpdateThumbnailProperties(_handle, ref properties) >= 0;
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
        {
            _ = DwmUnregisterThumbnail(handle);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(
        nint destinationWindow,
        nint sourceWindow,
        out nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(
        nint thumbnail,
        out NativeSize size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(
        nint thumbnail,
        ref DwmThumbnailProperties properties);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public NativeRectangle Destination;
        public NativeRectangle Source;
        public byte Opacity;

        [MarshalAs(UnmanagedType.Bool)]
        public bool IsVisible;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }
}
