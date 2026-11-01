using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.Graphics;

namespace SeanShell.App;

/// <summary>A transient, non-activating status surface; never an AppBar.</summary>
public sealed partial class ActivityIslandWindow : Window
{
    private readonly DispatcherQueueTimer _dismissTimer;
    private Storyboard? _entrance;
    private bool _allowClose;
    private bool _reducedEffects;
    private long _dismissAt;

    public ActivityIslandWindow()
    {
        InitializeComponent();
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        _ = SetWindowLongPtr(handle, -20, new nint(style | 0x00000080L | 0x08000000L));
        _ = DwmWindowChrome.TryConfigureFloatingSurface(handle);
        _dismissTimer = DispatcherQueue.CreateTimer();
        _dismissTimer.IsRepeating = false;
        _dismissTimer.Tick += OnDismissTick;
        AppWindow.Closing += OnClosing;
        SetReducedEffects(false);
    }

    public bool IsVisible { get; private set; }

    public nint MonitorHandle { get; private set; }

    public void SetReducedEffects(bool enabled)
    {
        _reducedEffects = enabled;
        StopEntrance();
        SystemBackdrop = enabled ? null : new DesktopAcrylicBackdrop();
        IslandRoot.Background = enabled
            ? (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"]
            : new SolidColorBrush(global::Windows.UI.Color.FromArgb(230, 12, 16, 24));
        IslandRoot.BorderBrush = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
        IdentityTile.Background = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
        ActivityGlyph.Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        var foreground = enabled
            ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.White);
        ActivityTitle.Foreground = foreground;
        ActivityDetail.Foreground = foreground;
        ActivityDetail.Opacity = 0.72;
        ActivityLabel.Foreground = foreground;
        ActivityLabel.Opacity = 0.72;
    }

    public void ShowActivity(string title, string detail, DisplayMonitorSnapshot monitor, double textScaleFactor)
    {
        if (_allowClose)
        {
            return;
        }

        _dismissTimer.Stop();
        StopEntrance();
        ActivityTitle.Text = title;
        ActivityDetail.Text = detail;
        AutomationProperties.SetName(IslandRoot, $"Now active: {title}. {detail}");
        var bounds = ActivityIslandLayout.Calculate(monitor, DisplayDpiService.GetScaleFactor(monitor.Handle), textScaleFactor);
        // Moving before showing avoids a flash at the previous monitor's position.
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = DwmWindowChrome.TryApplyRoundedClip(handle, bounds.Width, bounds.Height, bounds.Height / 2, 0);
        MonitorHandle = monitor.Handle;
        IsVisible = true;
        AppWindow.Show(false);
        if (!_reducedEffects)
        {
            _entrance = new Storyboard();
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(180) };
            Storyboard.SetTarget(fade, IslandContent);
            Storyboard.SetTargetProperty(fade, "Opacity");
            _entrance.Children.Add(fade);
            var slide = new DoubleAnimation { From = -6, To = 0, Duration = TimeSpan.FromMilliseconds(180) };
            Storyboard.SetTarget(slide, EntranceTranslate);
            Storyboard.SetTargetProperty(slide, "Y");
            _entrance.Children.Add(slide);
            _entrance.Begin();
        }

        _dismissAt = Environment.TickCount64 + 2400;
        _dismissTimer.Interval = TimeSpan.FromMilliseconds(2400);
        _dismissTimer.Start();
    }

    public void Dismiss()
    {
        if (_allowClose)
        {
            return;
        }

        _dismissTimer.Stop();
        StopEntrance();
        if (IsVisible)
        {
            AppWindow.Hide();
            IsVisible = false;
        }
    }

    public void Shutdown()
    {
        if (_allowClose)
        {
            return;
        }

        Dismiss();
        _allowClose = true;
        _dismissTimer.Tick -= OnDismissTick;
        Close();
    }

    private void StopEntrance()
    {
        _entrance?.Stop();
        _entrance = null;
        IslandContent.Opacity = 1;
        EntranceTranslate.Y = 0;
    }

    private void OnDismissTick(DispatcherQueueTimer sender, object args)
    {
        if (_allowClose)
        {
            return;
        }

        // A queued tick from a replaced notification must not dismiss its successor.
        var remaining = _dismissAt - Environment.TickCount64;
        if (IsVisible && remaining > 0)
        {
            sender.Interval = TimeSpan.FromMilliseconds(remaining);
            sender.Start();
            return;
        }
        Dismiss();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args) => Dismiss();

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            args.Cancel = true;
            Dismiss();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
