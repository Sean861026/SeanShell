using System.Runtime.InteropServices;

namespace SeanShell.Windows;

public static class DwmWindowChrome
{
    private const int WindowLongStyle = -16;
    private const int WindowLongExtendedStyle = -20;
    private const int WindowStyleCaption = 0x00C00000;
    private const int WindowStyleThickFrame = 0x00040000;
    private const int WindowExtendedStyleDialogModalFrame = 0x00000001;
    private const int WindowExtendedStyleWindowEdge = 0x00000100;
    private const int WindowExtendedStyleClientEdge = 0x00000200;
    private const int WindowExtendedStyleStaticEdge = 0x00020000;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const int DwmWindowAttributeNonClientRenderingPolicy = 2;
    private const int DwmNonClientRenderingDisabled = 1;
    private const int DwmWindowAttributeCornerPreference = 33;
    private const int DwmWindowAttributeBorderColor = 34;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;

    public static bool TryConfigureFloatingSurface(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        var style = GetWindowLong(windowHandle, WindowLongStyle);
        var borderlessStyle = style &
            ~(WindowStyleCaption | WindowStyleThickFrame);
        _ = SetWindowLong(windowHandle, WindowLongStyle, borderlessStyle);

        var extendedStyle = GetWindowLong(windowHandle, WindowLongExtendedStyle);
        var borderlessExtendedStyle = extendedStyle &
            ~(WindowExtendedStyleDialogModalFrame |
              WindowExtendedStyleWindowEdge |
              WindowExtendedStyleClientEdge |
              WindowExtendedStyleStaticEdge);
        _ = SetWindowLong(
            windowHandle,
            WindowLongExtendedStyle,
            borderlessExtendedStyle);

        var frameRefreshed = SetWindowPos(
            windowHandle,
            0,
            0,
            0,
            0,
            0,
            SetWindowPositionNoSize |
            SetWindowPositionNoMove |
            SetWindowPositionNoZOrder |
            SetWindowPositionNoActivate |
            SetWindowPositionFrameChanged);

        var nonClientRenderingPolicy = DwmNonClientRenderingDisabled;
        var nonClientResult = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowAttributeNonClientRenderingPolicy,
            ref nonClientRenderingPolicy,
            sizeof(int));

        var borderColor = DwmColorNone;
        var borderResult = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowAttributeBorderColor,
            ref borderColor,
            sizeof(uint));

        var cornerPreference = DwmWindowCornerPreferenceRound;
        var cornerResult = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowAttributeCornerPreference,
            ref cornerPreference,
            sizeof(int));

        return frameRefreshed &&
            nonClientResult == 0 &&
            borderResult == 0 &&
            cornerResult == 0;
    }

    public static bool TryApplyRoundedClip(
        nint windowHandle,
        int width,
        int height,
        int cornerRadius,
        int inset)
    {
        if (windowHandle == 0 ||
            width <= inset * 2 ||
            height <= inset * 2 ||
            cornerRadius <= 0 ||
            inset < 0)
        {
            return false;
        }

        var region = CreateRoundRectRgn(
            inset,
            inset,
            width - inset,
            height - inset,
            cornerRadius * 2,
            cornerRadius * 2);
        if (region == 0)
        {
            return false;
        }

        if (SetWindowRgn(windowHandle, region, redraw: true) != 0)
        {
            return true;
        }

        _ = DeleteObject(region);
        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(
        nint windowHandle,
        int index,
        int newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(
        nint windowHandle,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref uint value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
