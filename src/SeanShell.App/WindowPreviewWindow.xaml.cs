using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.Graphics;

namespace SeanShell.App;

public sealed partial class WindowPreviewWindow : Window
{
    private const int ExtendedWindowStyle = -20;
    private const long ToolWindowStyle = 0x00000080L;
    private const long NoActivateStyle = 0x08000000L;
    private readonly DesktopWindowService _windowService;
    private readonly List<PreviewEntry> _entries = [];
    private bool _allowClose;

    public WindowPreviewWindow(DesktopWindowService windowService)
    {
        _windowService = windowService;
        InitializeComponent();
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        ConfigureNativeWindow();
        AppWindow.Closing += OnWindowClosing;
    }

    public bool IsVisible { get; private set; }

    public event EventHandler? PreviewEntered;

    public event EventHandler? PreviewExited;

    public event EventHandler? Dismissed;

    public void Show(
        DockItemViewModel item,
        int anchorCenterX,
        int dockTop,
        DisplayMonitorSnapshot monitor,
        double rasterizationScale)
    {
        ClearEntries();
        var layout = WindowPreviewLayout.Calculate(item.WindowCount);
        if (layout.VisibleCount == 0)
        {
            Dismiss();
            return;
        }

        ConfigureGrid(layout);
        var windows = item.Windows.Take(layout.VisibleCount).ToArray();
        for (var index = 0; index < windows.Length; index++)
        {
            AddPreviewCard(windows[index], index, layout.Columns);
        }

        var scale = Math.Max(1, rasterizationScale);
        var pixelWidth = ToPixels(layout.Width, scale);
        var pixelHeight = ToPixels(layout.Height, scale);
        var x = Math.Clamp(
            anchorCenterX - (pixelWidth / 2),
            monitor.WorkAreaX,
            Math.Max(monitor.WorkAreaX, monitor.WorkAreaX + monitor.WorkAreaWidth - pixelWidth));
        var maximumY = Math.Max(
            monitor.WorkAreaY,
            monitor.WorkAreaY + monitor.WorkAreaHeight - pixelHeight);
        var preferredY = dockTop - pixelHeight - 8;
        var fallbackY = dockTop + 8;
        var y = Math.Clamp(
            preferredY >= monitor.WorkAreaY ? preferredY : fallbackY,
            monitor.WorkAreaY,
            maximumY);

        AppWindow.Resize(new SizeInt32(pixelWidth, pixelHeight));
        AppWindow.Move(new PointInt32(x, y));
        AppWindow.Show(false);
        IsVisible = true;
        PreviewRoot.UpdateLayout();
        DispatcherQueue.TryEnqueue(UpdateThumbnails);
    }

    public void Dismiss()
    {
        if (!IsVisible)
        {
            return;
        }

        ClearEntries();
        AppWindow.Hide();
        IsVisible = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        ClearEntries();
        IsVisible = false;
        _allowClose = true;
        Close();
    }

    private void ConfigureGrid(WindowPreviewLayoutResult layout)
    {
        PreviewGrid.Children.Clear();
        PreviewGrid.ColumnDefinitions.Clear();
        PreviewGrid.RowDefinitions.Clear();
        for (var column = 0; column < layout.Columns; column++)
        {
            PreviewGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(WindowPreviewLayout.CardWidth),
            });
        }

        for (var row = 0; row < layout.Rows; row++)
        {
            PreviewGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(WindowPreviewLayout.CardHeight),
            });
        }
    }

    private void AddPreviewCard(
        DesktopWindowSnapshot window,
        int index,
        int columns)
    {
        var card = new Grid
        {
            Background = Application.Current.Resources[
                "CardBackgroundFillColorDefaultBrush"] as Brush,
            CornerRadius = new CornerRadius(12),
        };
        card.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(48),
        });
        card.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        var header = new Grid
        {
            Padding = new Thickness(12, 2, 2, 2),
            ColumnSpacing = 8,
        };
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        var title = new TextBlock
        {
            Text = window.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var closeButton = new Button
        {
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                Glyph = "\uE8BB",
            },
        };
        AutomationProperties.SetName(closeButton, $"Close {window.Title}");
        ToolTipService.SetToolTip(closeButton, "Close window");
        closeButton.Click += (_, _) =>
        {
            _ = _windowService.RequestClose(window.Handle);
            Dismiss();
        };
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);
        Grid.SetRow(header, 0);
        card.Children.Add(header);

        var surface = new Button
        {
            Margin = new Thickness(8, 0, 8, 8),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(
                Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var fallback = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
            Children =
            {
                new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 22,
                    Glyph = "\uE7B8",
                },
                new TextBlock
                {
                    Text = "Preview unavailable",
                    Foreground = Application.Current.Resources[
                        "TextFillColorSecondaryBrush"] as Brush,
                },
            },
        };
        surface.Content = fallback;
        AutomationProperties.SetName(
            surface,
            $"Switch to {window.Title}, {window.ProcessName}");
        AutomationProperties.SetHelpText(
            surface,
            window.IsMinimized ? "Window is minimized." : "Window is running.");
        surface.Click += (_, _) =>
        {
            _ = _windowService.RestoreAndActivate(window.Handle);
            Dismiss();
        };
        Grid.SetRow(surface, 1);
        card.Children.Add(surface);

        Grid.SetColumn(card, index % columns);
        Grid.SetRow(card, index / columns);
        PreviewGrid.Children.Add(card);
        _entries.Add(new PreviewEntry(window.Handle, surface, fallback));
    }

    private void UpdateThumbnails()
    {
        if (!IsVisible)
        {
            return;
        }

        var destinationWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = PreviewRoot.XamlRoot?.RasterizationScale ?? GetScale();
        foreach (var entry in _entries)
        {
            entry.Thumbnail?.Dispose();
            entry.Thumbnail = null;
            if (!DwmThumbnail.TryCreate(
                    destinationWindow,
                    entry.SourceWindow,
                    out var thumbnail) ||
                thumbnail is null)
            {
                continue;
            }

            var point = entry.Surface
                .TransformToVisual(PreviewRoot)
                .TransformPoint(new global::Windows.Foundation.Point(0, 0));
            var destination = new WindowPreviewRectangle(
                ToPixels(point.X, scale),
                ToPixels(point.Y, scale),
                ToPixels(entry.Surface.ActualWidth, scale),
                ToPixels(entry.Surface.ActualHeight, scale));
            if (!thumbnail.TryShow(destination))
            {
                thumbnail.Dispose();
                continue;
            }

            entry.Thumbnail = thumbnail;
            entry.Fallback.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearEntries()
    {
        foreach (var entry in _entries)
        {
            entry.Thumbnail?.Dispose();
        }

        _entries.Clear();
        PreviewGrid.Children.Clear();
    }

    private double GetScale()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return Math.Max(1, GetDpiForWindow(handle)) / 96d;
    }

    private void ConfigureNativeWindow()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = GetWindowLongPtr(handle, ExtendedWindowStyle).ToInt64();
        _ = SetWindowLongPtr(
            handle,
            ExtendedWindowStyle,
            new nint(style | ToolWindowStyle | NoActivateStyle));
    }

    private static int ToPixels(double value, double scale) =>
        Math.Max(1, (int)Math.Round(value * scale));

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        PreviewEntered?.Invoke(this, EventArgs.Empty);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        PreviewExited?.Invoke(this, EventArgs.Empty);

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        Dismiss();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(
        nint window,
        int index,
        nint newValue);

    private sealed class PreviewEntry(
        nint sourceWindow,
        FrameworkElement surface,
        FrameworkElement fallback)
    {
        public nint SourceWindow { get; } = sourceWindow;

        public FrameworkElement Surface { get; } = surface;

        public FrameworkElement Fallback { get; } = fallback;

        public DwmThumbnail? Thumbnail { get; set; }
    }
}
