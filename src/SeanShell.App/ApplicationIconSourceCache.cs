using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SeanShell.Core;

namespace SeanShell.App;

internal static class ApplicationIconSourceCache
{
    private static readonly ConditionalWeakTable<ApplicationIconSnapshot, ImageSource>
        Sources = new();

    public static ImageSource? Get(ApplicationIconSnapshot? icon) =>
        icon is null ? null : Sources.GetValue(icon, Create);

    private static ImageSource Create(ApplicationIconSnapshot icon)
    {
        var bitmap = new WriteableBitmap(icon.Width, icon.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Position = 0;
            stream.Write(icon.BgraPixels.Span);
            stream.Flush();
        }

        bitmap.Invalidate();
        return bitmap;
    }
}
