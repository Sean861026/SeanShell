using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SeanShell.Core;
using Windows.Graphics.Imaging;

namespace SeanShell.App;

internal static class ApplicationIconSourceCache
{
    private static readonly ConditionalWeakTable<ApplicationIconSnapshot, ImageSource>
        Sources = new();
    private static readonly ConditionalWeakTable<ImageSource, SoftwareBitmap>
        SourceBitmaps = new();
    private static readonly object Gate = new();

    public static async Task<ImageSource?> GetAsync(ApplicationIconSnapshot? icon)
    {
        if (icon is null)
        {
            return null;
        }

        lock (Gate)
        {
            if (Sources.TryGetValue(icon, out var cached))
            {
                return cached;
            }
        }

        try
        {
            var pixels = icon.BgraPixels.ToArray();
            var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                pixels.AsBuffer(),
                BitmapPixelFormat.Bgra8,
                icon.Width,
                icon.Height,
                BitmapAlphaMode.Premultiplied);
            var source = new SoftwareBitmapSource();
            // XAML may copy the pixels to its surface after SetBitmapAsync
            // returns. Closing the bitmap here can fail-fast that worker with
            // RO_E_CLOSED, so keep it alive for as long as the source is used.
            SourceBitmaps.Add(source, bitmap);
            await source.SetBitmapAsync(bitmap);

            lock (Gate)
            {
                if (Sources.TryGetValue(icon, out var cached))
                {
                    return cached;
                }

                Sources.Add(icon, source);
                return source;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Unable to create an application icon bitmap. {exception}");
            return null;
        }
    }
}
