using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
    private readonly DispatcherQueueTimer _thumbnailRetryTimer;
    private Storyboard? _entranceTransition;
    private bool _allowClose;
    private bool _reducedEffects;
    private int _thumbnailAttempts;
    private bool _thumbnailUpdateQueued;

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
        _thumbnailRetryTimer = DispatcherQueue.CreateTimer();
        _thumbnailRetryTimer.Interval = WindowPreviewRetryPolicy.Delay;
        _thumbnailRetryTimer.IsRepeating = false;
        _thumbnailRetryTimer.Tick += OnThumbnailRetryTimerTick;
        PreviewRoot.SizeChanged += OnPreviewRootSizeChanged;
        AppWindow.Closing += OnWindowClosing;
    }

    public bool IsVisible { get; private set; }

    public event EventHandler? PreviewEntered;

    public event EventHandler? PreviewExited;

    public event EventHandler? Dismissed;

    public void SetReducedEffects(bool enabled)
    {
        _reducedEffects = enabled;
        if (enabled)
        {
            StopEntranceTransition();
        }

        SystemBackdrop = enabled
            ? null
            : new DesktopAcrylicBackdrop();
        PreviewRoot.Background = Application.Current.Resources[
            enabled
                ? "CardBackgroundFillColorDefaultBrush"
                : "LayerOnAcrylicFillColorDefaultBrush"] as Brush;
    }

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

        IsVisible = true;
        _thumbnailAttempts = 0;
        AppWindow.Show(false);
        AppWindow.MoveAndResize(new RectInt32(x, y, pixelWidth, pixelHeight));
        BeginEntranceTransition();
        QueueThumbnailUpdate();
    }

    public void Dismiss()
    {
        if (!IsVisible)
        {
            return;
        }

        ClearEntries();
        StopEntranceTransition();
        AppWindow.Hide();
        IsVisible = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        ClearEntries();
        StopEntranceTransition();
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

    private void BeginEntranceTransition()
    {
        StopEntranceTransition();
        var motion = WindowPreviewEntranceMotion.Resolve(_reducedEffects);
        PreviewRoot.Opacity = motion.StartOpacity;
        PreviewTranslate.Y = motion.StartTranslationY;
        if (motion.DurationMilliseconds == 0)
        {
            PreviewRoot.Opacity = motion.EndOpacity;
            PreviewTranslate.Y = motion.EndTranslationY;
            return;
        }

        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseOut,
        };
        var duration = new Duration(
            TimeSpan.FromMilliseconds(motion.DurationMilliseconds));
        var opacity = new DoubleAnimation
        {
            From = motion.StartOpacity,
            To = motion.EndOpacity,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(opacity, PreviewRoot);
        Storyboard.SetTargetProperty(opacity, nameof(UIElement.Opacity));
        var translation = new DoubleAnimation
        {
            From = motion.StartTranslationY,
            To = motion.EndTranslationY,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(translation, PreviewTranslate);
        Storyboard.SetTargetProperty(
            translation,
            nameof(TranslateTransform.Y));

        var transition = new Storyboard();
        transition.Children.Add(opacity);
        transition.Children.Add(translation);
        transition.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_entranceTransition, transition))
            {
                return;
            }

            PreviewRoot.Opacity = motion.EndOpacity;
            PreviewTranslate.Y = motion.EndTranslationY;
            _entranceTransition = null;
            transition.Stop();
        };
        _entranceTransition = transition;
        transition.Begin();
    }

    private void StopEntranceTransition()
    {
        _entranceTransition?.Stop();
        _entranceTransition = null;
        PreviewRoot.Opacity = 1;
        PreviewTranslate.Y = 0;
    }

    private void AddPreviewCard(
        DesktopWindowSnapshot window,
        int index,
        int columns)
    {
        var neutralCardStroke = Application.Current.Resources[
            "CardStrokeColorDefaultBrush"] as Brush;
        var accentCardStroke = Application.Current.Resources[
            "AccentFillColorDefaultBrush"] as Brush;
        var presentation = WindowPreviewCardPresentation.Resolve(
            window.IsMinimized,
            window.IsForeground);
        var defaultCardStroke = presentation.UsesAccentStroke
            ? accentCardStroke
            : neutralCardStroke;
        var card = new Grid
        {
            Background = Application.Current.Resources[
                "CardBackgroundFillColorDefaultBrush"] as Brush,
            BorderBrush = defaultCardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
        };
        card.PointerEntered += (_, _) => card.BorderBrush = accentCardStroke;
        card.PointerExited += (_, _) => card.BorderBrush = defaultCardStroke;
        card.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(56),
        });
        card.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        var header = new Grid
        {
            Padding = new Thickness(10, 4, 4, 4),
            ColumnSpacing = 10,
        };
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        var iconImage = new Image
        {
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
        };
        var iconFallback = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Glyph = "\uE737",
        };
        var iconFallbackTile = new Border
        {
            Width = 36,
            Height = 36,
            Background = Application.Current.Resources[
                "AccentFillColorSecondaryBrush"] as Brush,
            CornerRadius = new CornerRadius(10),
            Child = iconFallback,
        };
        var iconTile = new Border
        {
            Width = 36,
            Height = 36,
            Child = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    iconFallbackTile,
                    iconImage,
                },
            },
        };
        Grid.SetColumn(iconTile, 0);
        header.Children.Add(iconTile);
        _ = LoadPreviewIconAsync(iconImage, iconFallbackTile, window.Icon);

        var title = new TextBlock
        {
            Text = window.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var status = new TextBlock
        {
            Text = $"{window.ProcessName} · {presentation.StatusLabel}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            Foreground = Application.Current.Resources[
                "TextFillColorSecondaryBrush"] as Brush,
        };
        var identity = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1,
            Children =
            {
                title,
                status,
            },
        };
        Grid.SetColumn(identity, 1);
        header.Children.Add(identity);

        var closeGlyph = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources[
                "TextFillColorSecondaryBrush"] as Brush,
            Glyph = "\uE8BB",
        };
        var closeChrome = new Border
        {
            Width = 32,
            Height = 32,
            Background = new SolidColorBrush(
                Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(8),
            Child = closeGlyph,
        };
        var closeButton = new Button
        {
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(
                Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Content = closeChrome,
        };
        var closePointerOver = false;
        var closeFocused = false;
        void UpdateCloseEmphasis()
        {
            var emphasized = closePointerOver || closeFocused;
            closeChrome.Background = emphasized
                ? Application.Current.Resources[
                    "SystemFillColorCriticalBackgroundBrush"] as Brush
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            closeGlyph.Foreground = Application.Current.Resources[
                emphasized
                    ? "SystemFillColorCriticalBrush"
                    : "TextFillColorSecondaryBrush"] as Brush;
        }

        closeButton.PointerEntered += (_, _) =>
        {
            closePointerOver = true;
            UpdateCloseEmphasis();
        };
        closeButton.PointerExited += (_, _) =>
        {
            closePointerOver = false;
            UpdateCloseEmphasis();
        };
        closeButton.GotFocus += (_, _) =>
        {
            closeFocused = true;
            UpdateCloseEmphasis();
        };
        closeButton.LostFocus += (_, _) =>
        {
            closeFocused = false;
            UpdateCloseEmphasis();
        };
        AutomationProperties.SetName(closeButton, $"Close {window.Title}");
        ToolTipService.SetToolTip(closeButton, "Close window");
        closeButton.Click += (_, _) =>
        {
            _ = _windowService.RequestClose(window.Handle);
            Dismiss();
        };
        Grid.SetColumn(closeButton, 2);
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
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var fallback = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var loadingIndicator = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 22,
            Foreground = Application.Current.Resources[
                "TextFillColorSecondaryBrush"] as Brush,
            Glyph = "\uE895",
        };
        fallback.Children.Add(loadingIndicator);
        var unavailableIndicator = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
            Visibility = Visibility.Collapsed,
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
        fallback.Children.Add(unavailableIndicator);
        surface.Content = fallback;
        AutomationProperties.SetName(
            surface,
            $"Switch to {window.Title}, {window.ProcessName}");
        var helpText = window.IsMinimized
            ? "Window is minimized."
            : "Window is running.";
        AutomationProperties.SetHelpText(surface, helpText);
        surface.Click += (_, _) =>
        {
            _ = _windowService.RestoreAndActivate(window.Handle);
            Dismiss();
        };
        surface.SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                QueueThumbnailUpdate();
            }
        };
        Grid.SetRow(surface, 1);
        card.Children.Add(surface);

        Grid.SetColumn(card, index % columns);
        Grid.SetRow(card, index / columns);
        PreviewGrid.Children.Add(card);
        _entries.Add(new PreviewEntry(
            window.Handle,
            surface,
            fallback,
            loadingIndicator,
            unavailableIndicator,
            helpText));
    }

    private static async Task LoadPreviewIconAsync(
        Image image,
        FrameworkElement fallback,
        ApplicationIconSnapshot? snapshot)
    {
        var source = await ApplicationIconSourceCache.GetAsync(snapshot);
        if (source is null)
        {
            return;
        }

        image.Source = source;
        image.Visibility = Visibility.Visible;
        fallback.Visibility = Visibility.Collapsed;
    }

    private void UpdateThumbnails()
    {
        _thumbnailUpdateQueued = false;
        if (!IsVisible)
        {
            return;
        }

        var destinationWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = PreviewRoot.XamlRoot?.RasterizationScale ?? GetScale();
        var hasUnresolvedThumbnail = false;
        _thumbnailAttempts++;
        foreach (var entry in _entries)
        {
            // A newly shown WinUI window can report the button's desired size
            // before its first arrange pass. Waiting here prevents DWM from
            // permanently receiving a tiny fallback-sized destination.
            if (entry.Surface.ActualWidth < WindowPreviewLayout.CardWidth / 2d ||
                entry.Surface.ActualHeight < WindowPreviewLayout.CardHeight / 3d)
            {
                hasUnresolvedThumbnail = true;
                continue;
            }

            entry.Thumbnail?.Dispose();
            entry.Thumbnail = null;
            if (!DwmThumbnail.TryCreate(
                    destinationWindow,
                    entry.SourceWindow,
                    out var thumbnail) ||
                thumbnail is null)
            {
                hasUnresolvedThumbnail = true;
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
                hasUnresolvedThumbnail = true;
                continue;
            }

            entry.Thumbnail = thumbnail;
        }

        var retryScheduled = WindowPreviewRetryPolicy.ShouldRetry(
            hasUnresolvedThumbnail,
            _thumbnailAttempts);
        foreach (var entry in _entries)
        {
            entry.ApplyFallback(WindowPreviewFallbackPresentation.Resolve(
                thumbnailAvailable: entry.Thumbnail is not null,
                retryScheduled));
        }

        if (retryScheduled)
        {
            _thumbnailRetryTimer.Stop();
            _thumbnailRetryTimer.Start();
        }
    }

    private void QueueThumbnailUpdate()
    {
        if (_thumbnailUpdateQueued)
        {
            return;
        }

        _thumbnailUpdateQueued = true;
        DispatcherQueue.TryEnqueue(UpdateThumbnails);
    }

    private void OnPreviewRootSizeChanged(
        object sender,
        SizeChangedEventArgs args)
    {
        if (IsVisible)
        {
            QueueThumbnailUpdate();
        }
    }

    private void OnThumbnailRetryTimerTick(
        DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        QueueThumbnailUpdate();
    }

    private void ClearEntries()
    {
        _thumbnailRetryTimer.Stop();
        _thumbnailAttempts = 0;
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
        FrameworkElement fallback,
        FrameworkElement loadingIndicator,
        FrameworkElement unavailableIndicator,
        string helpText)
    {
        public nint SourceWindow { get; } = sourceWindow;

        public FrameworkElement Surface { get; } = surface;

        public FrameworkElement Fallback { get; } = fallback;

        public FrameworkElement LoadingIndicator { get; } = loadingIndicator;

        public FrameworkElement UnavailableIndicator { get; } = unavailableIndicator;

        public string HelpText { get; } = helpText;

        public DwmThumbnail? Thumbnail { get; set; }

        public void ApplyFallback(WindowPreviewFallbackState state)
        {
            Fallback.Visibility = state == WindowPreviewFallbackState.Hidden
                ? Visibility.Collapsed
                : Visibility.Visible;
            LoadingIndicator.Visibility = state == WindowPreviewFallbackState.Loading
                ? Visibility.Visible
                : Visibility.Collapsed;
            UnavailableIndicator.Visibility = state == WindowPreviewFallbackState.Unavailable
                ? Visibility.Visible
                : Visibility.Collapsed;

            var stateDescription = state switch
            {
                WindowPreviewFallbackState.Loading => " Live preview is loading.",
                WindowPreviewFallbackState.Unavailable =>
                    " Live preview is unavailable; select to switch to this window.",
                _ => string.Empty,
            };
            AutomationProperties.SetHelpText(Surface, HelpText + stateDescription);
        }
    }
}
