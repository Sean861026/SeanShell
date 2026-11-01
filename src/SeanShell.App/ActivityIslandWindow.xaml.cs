using System.ComponentModel;
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
    private const int ExtendedWindowStyle = -20;
    private const int ToolWindowStyle = 0x00000080;
    private const int NoActivateStyle = 0x08000000;
    private readonly DispatcherQueueTimer _dismissTimer;
    private readonly ActivityIslandPresentationState _presentation = new();
    private Storyboard? _entrance;
    private bool _allowClose;
    private bool _reducedEffects;

    public ActivityIslandWindow()
    {
        InitializeComponent();
        _dismissTimer = DispatcherQueue.CreateTimer();
        _dismissTimer.IsRepeating = false;
        _dismissTimer.Tick += OnDismissTick;
        try
        {
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            AppWindow.SetPresenter(presenter);
            AppWindow.IsShownInSwitchers = false;
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ConfigureNonActivatingWindow(handle);
            _ = DwmWindowChrome.TryConfigureFloatingSurface(handle);
            AppWindow.Closing += OnClosing;
            ApplyReducedEffects(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Shutdown();
            throw;
        }
    }

    public bool IsVisible { get; private set; }

    public bool IsClosed => _allowClose;

    public nint MonitorHandle { get; private set; }

    public void SetReducedEffects(bool enabled)
    {
        if (!_allowClose && !ActivityIslandCleanup.TryRun(() => ApplyReducedEffects(enabled), ReportFailure))
        {
            Shutdown();
        }
    }

    private void ApplyReducedEffects(bool enabled)
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

    public void ShowActivity(ActivityIslandContent content, DisplayMonitorSnapshot monitor, double textScaleFactor)
    {
        if (_allowClose)
        {
            return;
        }

        var change = _presentation.Present(content, monitor.Handle, Environment.TickCount64);
        if (change == ActivityIslandPresentationChange.Unchanged)
        {
            return;
        }

        _dismissTimer.Stop();
        StopEntrance();
        ActivityLabel.Text = content.Label;
        ActivityGlyph.Glyph = content.Glyph;
        ActivityTitle.Text = content.Title;
        ActivityDetail.Text = content.Detail;
        AutomationProperties.SetName(IslandRoot, content.AccessibleName);
        var bounds = ActivityIslandLayout.Calculate(monitor, DisplayDpiService.GetScaleFactor(monitor.Handle), textScaleFactor);
        // Moving before showing avoids a flash at the previous monitor's position.
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = DwmWindowChrome.TryApplyRoundedClip(handle, bounds.Width, bounds.Height, bounds.Height / 2, 0);
        MonitorHandle = monitor.Handle;
        IsVisible = true;
        if (change == ActivityIslandPresentationChange.Show)
        {
            AppWindow.Show(false);
        }
        if (change == ActivityIslandPresentationChange.Show && !_reducedEffects)
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

        _dismissTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Max(1, _presentation.RemainingMilliseconds(Environment.TickCount64)));
        _dismissTimer.Start();
    }

    public void Dismiss()
    {
        if (_allowClose)
        {
            return;
        }

        var wasVisible = IsVisible;
        IsVisible = false;
        _presentation.Dismiss();
        var succeeded = ActivityIslandCleanup.TryRun(_dismissTimer.Stop, ReportFailure);
        succeeded = ActivityIslandCleanup.TryRun(StopEntrance, ReportFailure) && succeeded;
        if (wasVisible)
        {
            succeeded = ActivityIslandCleanup.TryRun(() => AppWindow.Hide(), ReportFailure) && succeeded;
        }
        if (!succeeded)
        {
            Shutdown();
        }
    }

    public void Shutdown()
    {
        if (_allowClose)
        {
            return;
        }

        _allowClose = true;
        IsVisible = false;
        _presentation.Dismiss();
        _ = ActivityIslandCleanup.TryRun(_dismissTimer.Stop, ReportFailure);
        _ = ActivityIslandCleanup.TryRun(() => _dismissTimer.Tick -= OnDismissTick, ReportFailure);
        var entrance = _entrance;
        _entrance = null;
        _ = ActivityIslandCleanup.TryRun(() => entrance?.Stop(), ReportFailure);
        _ = ActivityIslandCleanup.TryRun(() => AppWindow.Hide(), ReportFailure);
        _ = ActivityIslandCleanup.TryRun(Close, ReportFailure);
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

        if (!ActivityIslandCleanup.TryRun(() =>
        {
            // A queued tick from a replaced notification must not dismiss its successor.
            var remaining = _presentation.RemainingMilliseconds(Environment.TickCount64);
            if (IsVisible && remaining > 0)
            {
                sender.Interval = TimeSpan.FromMilliseconds(remaining);
                sender.Start();
                return;
            }
            Dismiss();
        }, ReportFailure))
        {
            Shutdown();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args) => Dismiss();

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            if (!ActivityIslandCleanup.TryRun(() =>
            {
                args.Cancel = true;
                Dismiss();
            }, ReportFailure))
            {
                Shutdown();
            }
        }
    }

    private static void ReportFailure(Exception exception) =>
        _ = ActivityIslandCleanup.TryRun(() =>
            ((App)Application.Current).Diagnostics.TryWrite(DiagnosticEventKind.DockActionFailed, exception));

    private static void ConfigureNonActivatingWindow(nint handle)
    {
        const int requiredStyle = ToolWindowStyle | NoActivateStyle;
        var style = GetWindowLong(handle, ExtendedWindowStyle);
        Marshal.SetLastPInvokeError(0);
        var previousStyle = SetWindowLong(handle, ExtendedWindowStyle, style | requiredStyle);
        var error = Marshal.GetLastPInvokeError();
        if (previousStyle == 0 && error != 0)
        {
            throw new Win32Exception(error);
        }

        if ((GetWindowLong(handle, ExtendedWindowStyle) & requiredStyle) != requiredStyle)
        {
            throw new InvalidOperationException("The non-activating Island window style was not applied.");
        }
    }

    // GWL_EXSTYLE is a 32-bit value on every architecture; the W entry points
    // exist on x86 as well as x64/ARM64, unlike Get/SetWindowLongPtrW exports.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);
}
