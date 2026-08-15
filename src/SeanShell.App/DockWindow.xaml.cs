using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.Text;

namespace SeanShell.App;

public sealed partial class DockWindow : Window
{
    private static readonly string[] BatteryGlyphs =
    [
        "\uE850", "\uE851", "\uE852", "\uE853", "\uE854", "\uE855",
        "\uE856", "\uE857", "\uE858", "\uE859", "\uE83F",
    ];
    private static readonly string[] ChargingBatteryGlyphs =
    [
        "\uE85A", "\uE85B", "\uE85C", "\uE85D", "\uE85E", "\uE85F",
        "\uE860", "\uE861", "\uE862", "\uE863", "\uE83E",
    ];
    private static readonly Lazy<ApplicationIconSnapshot?> LauncherMagnifierIcon =
        new(() => new NativeApplicationIconReader().ReadFileIcon(
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")));
    private const int DockHeight = 104;
    private const int PeekWidth = 180;
    private const int PeekHeight = 12;
    private const int CompactDockHeight = 92;
    private const int WorkAreaVerticalMargin = 16;
    private readonly DesktopWindowService _windowService;
    private readonly ShellStateStore _shellState;
    private readonly DisplayMonitorSnapshot _monitor;
    private readonly AudioEndpointService _audioEndpoint = new();
    private readonly SystemStatusSnapshotService _systemStatus = new();
    private readonly AppBarWorkAreaReservation _workAreaReservation = new();
    private readonly DispatcherQueueTimer _autoHideTimer;
    private readonly DispatcherQueueTimer _displayScaleRefreshTimer;
    private readonly DispatcherQueueTimer _previewDelayTimer;
    private readonly DispatcherQueueTimer _previewDismissTimer;
    private readonly HashSet<FrameworkElement> _dockMotionItems = [];
    private readonly HashSet<FrameworkElement> _activeDockNeighborItems = [];
    private readonly Dictionary<FrameworkElement, Storyboard> _launchFeedbackTransitions = [];
    private readonly bool _compactDensity;
    private double _displayScaleFactor;
    private double _textScaleFactor = 1;
    private bool _allowClose;
    private bool _autoHide = true;
    private bool _collapsed;
    private bool _contextMenuOpen;
    private bool _hasKeyboardFocus;
    private bool _immersiveSuppressed;
    private bool _modalDialogOpen;
    private bool _pointerInside;
    private bool _quickAudioControlsActive;
    private bool _reducedEffects;
    private bool _updatingQuickAudioControls;
    private int _expandedDockWidth;
    private DateTimeOffset _clockTimestamp = DateTimeOffset.Now;
    private AudioEndpointSnapshot _lastAudioStatus =
        new(false, null, false);
    private SystemStatusSnapshot _lastSystemStatus =
        new(null, false, null, null, false);
    private nint _returnFocusWindow;
    private IReadOnlyList<ShellCommand> _availableApplications = [];
    private IReadOnlyList<ShellCommand> _pinnedApplications = [];
    private IReadOnlyList<DesktopWindowSnapshot> _monitorWindows = [];
    private IReadOnlyList<string> _windowGroupOrder = [];
    private DockBounds? _reservedArea;
    private WindowPreviewWindow? _previewWindow;
    private LayeredDockIconWindow? _iconMagnifierWindow;
    private Storyboard? _visibilityTransition;
    private ScrollViewer? _windowListScrollViewer;
    private DockItemViewModel? _pendingPreviewItem;
    private FrameworkElement? _pendingPreviewAnchor;
    private FrameworkElement? _magnifiedElement;
    private IReadOnlyList<FrameworkElement> _magnifiedIconVisuals = [];

    public DockWindow(
        DesktopWindowService windowService,
        ShellStateStore shellState,
        DisplayMonitorSnapshot monitor,
        double textScaleFactor)
    {
        _windowService = windowService;
        _shellState = shellState;
        _monitor = monitor;
        InitializeComponent();
        Typography.SetNumeralAlignment(
            ClockTimeText,
            FontNumeralAlignment.Tabular);
        Typography.SetNumeralAlignment(
            ClockDateText,
            FontNumeralAlignment.Tabular);

        var density = ((App)Application.Current).SettingsLoad.Settings.DisplayDensity;
        _compactDensity = density == ShellDisplayDensity.Compact;
        _textScaleFactor = textScaleFactor;
        _displayScaleFactor = DisplayDpiService.GetScaleFactor(_monitor.Handle);
        _expandedDockWidth = CalculateExpandedWidth(0, 0);
        ApplyDisplayDensity(density);
        ApplyEmptyState(DockEmptyStatePresentation.Loading());
        ExpandedDock.Loaded += OnExpandedDockLoaded;
        PinnedList.ItemsSource = PinnedItems;
        WindowList.ItemsSource = Items;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigurePresenter();
        _ = DwmWindowChrome.TryConfigureFloatingSurface(
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        RefreshDockSystemIndicators();

        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(900);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += OnAutoHideTimerTick;

        _displayScaleRefreshTimer = DispatcherQueue.CreateTimer();
        _displayScaleRefreshTimer.Interval = TimeSpan.FromMilliseconds(500);
        _displayScaleRefreshTimer.IsRepeating = false;
        _displayScaleRefreshTimer.Tick += OnDisplayScaleRefreshTimerTick;

        _previewDelayTimer = DispatcherQueue.CreateTimer();
        _previewDelayTimer.Interval = TimeSpan.FromMilliseconds(450);
        _previewDelayTimer.IsRepeating = false;
        _previewDelayTimer.Tick += OnPreviewDelayTimerTick;

        _previewDismissTimer = DispatcherQueue.CreateTimer();
        _previewDismissTimer.Interval = TimeSpan.FromMilliseconds(320);
        _previewDismissTimer.IsRepeating = false;
        _previewDismissTimer.Tick += OnPreviewDismissTimerTick;

        _shellState.StateChanged += OnShellStateChanged;
        AppWindow.Closing += OnWindowClosing;
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public ObservableCollection<PinnedDockItemViewModel> PinnedItems { get; } = [];

    public nint MonitorHandle => _monitor.Handle;

    public event EventHandler? LauncherRequested;

    public event EventHandler? DashboardRequested;

    public event EventHandler? ShowDesktopRequested;

    public event EventHandler? SystemAreaRequested;

    public event EventHandler? ExitRequested;

    public event Func<ShellCommand, bool, Task<bool>>? PinChangedRequested;

    public event Func<ShellCommand, PinnedApplicationMoveDirection, Task<bool>>?
        PinMoveRequested;

    public event Func<IReadOnlyList<string>, Task<bool>>? PinOrderRequested;

    private void ApplyDisplayDensity(ShellDisplayDensity density)
    {
        if (density != ShellDisplayDensity.Compact)
        {
            return;
        }

        ExpandedDock.Padding = new Thickness(4);
        PinnedList.ItemContainerStyle =
            (Style)Application.Current.Resources["SeanCompactDockItemStyle"];
        WindowList.ItemContainerStyle =
            (Style)Application.Current.Resources["SeanCompactDockItemStyle"];
    }

    public void ShowDock()
    {
        if (_immersiveSuppressed)
        {
            return;
        }

        SetCollapsed(false);
        EmptyState.Visibility = Visibility.Visible;
        AppWindow.Show(false);
        ScheduleDisplayScaleRefresh();
        ScheduleAutoHide();
    }

    public void SetImmersiveSuppressed(bool suppressed)
    {
        if (_immersiveSuppressed == suppressed)
        {
            return;
        }

        _immersiveSuppressed = suppressed;
        if (suppressed)
        {
            _autoHideTimer.Stop();
            DismissWindowPreview();
            SetCollapsed(true);
            AppWindow.Show(false);
            return;
        }

        if (_shellState.Current.Mode != ShellMode.Gaming)
        {
            ShowDock();
        }
    }

    public void FocusDock()
    {
        _autoHideTimer.Stop();
        var foregroundWindow = _windowService.CaptureForegroundWindowHandle();
        var dockWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (foregroundWindow != 0 && foregroundWindow != dockWindow)
        {
            _returnFocusWindow = foregroundWindow;
        }

        SetCollapsed(false);
        AppWindow.Show();
        ScheduleDisplayScaleRefresh();
        Activate();
        _ = _windowService.RestoreAndActivate(dockWindow);
        _ = LauncherButton.Focus(FocusState.Keyboard);
    }

    public void SetAutoHide(bool enabled)
    {
        _autoHide = enabled;
        if (!enabled)
        {
            _autoHideTimer.Stop();
            SetCollapsed(false);
            return;
        }

        ScheduleAutoHide();
    }

    public void ApplyTextScaleFactor(double textScaleFactor)
    {
        _textScaleFactor = textScaleFactor;
        RefreshExpandedWidth();
        SetCollapsed(_collapsed);
    }

    public void SetReducedEffects(bool enabled)
    {
        _reducedEffects = enabled;
        if (enabled)
        {
            DismissDockMagnifier();
            StopVisibilityTransition();
            ApplyDockLayout(_collapsed);
        }

        SystemBackdrop = enabled
            ? null
            : new DesktopAcrylicBackdrop();
        ExpandedDock.Background = Application.Current.Resources[
            enabled
                ? "CardBackgroundFillColorDefaultBrush"
                : "SeanDockGlassShellBrush"] as Brush;
        ApplicationRegion.Background = Application.Current.Resources[
            enabled
                ? "CardBackgroundFillColorDefaultBrush"
                : "SeanDockGlassRegionBrush"] as Brush;
        SystemRegion.Background = Application.Current.Resources[
            enabled
                ? "CardBackgroundFillColorDefaultBrush"
                : "SeanDockGlassSystemBrush"] as Brush;
    }

    public WorkAreaReservationResult SetWorkAreaReservation(bool enabled)
    {
        if (!enabled)
        {
            var released = _workAreaReservation.Release();
            if (released.Success)
            {
                _reservedArea = null;
                SetCollapsed(_collapsed);
            }

            return released;
        }

        var dockHeight = ToPhysicalPixels(
            AccessibilityLayout.ScaleDockHeight(
                _compactDensity ? CompactDockHeight : DockHeight,
                _textScaleFactor));
        var result = _workAreaReservation.Reserve(
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            _monitor.Handle,
            dockHeight + ToPhysicalPixels(WorkAreaVerticalMargin));
        _reservedArea = result.Success ? result.ReservedArea : null;
        SetCollapsed(_collapsed);
        return result;
    }

    public void ApplyClock(DateTimeOffset timestamp)
    {
        _clockTimestamp = timestamp;
        var local = timestamp.LocalDateTime;
        var culture = CultureInfo.CurrentCulture;
        ClockTimeText.Text = local.ToString("t", culture);
        ClockDateText.Text = local.ToString("d", culture);
        var accessibleTimestamp =
            $"Current date and time: {local.ToString("F", culture)}";
        AutomationProperties.SetName(ClockButton, accessibleTimestamp);
        ClockDetailsText.Text = local.ToString("F", culture);
    }

    public void SetSystemAreaAccessState(bool available, bool revealed)
    {
        SystemAreaButton.Visibility =
            available ? Visibility.Visible : Visibility.Collapsed;
        var label = revealed
            ? "Resume SeanShell taskbar replacement"
            : "Show Windows system area";
        AutomationProperties.SetName(SystemAreaButton, label);
        ToolTipService.SetToolTip(
            SystemAreaButton,
            revealed
                ? "Hide the Windows taskbar and resume SeanShell replacement"
                : "Show the Windows taskbar for notification and system tray access");
        RevealSystemAreaIcon.Visibility =
            revealed ? Visibility.Collapsed : Visibility.Visible;
        ResumeTaskbarIcon.Visibility =
            revealed ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetShowDesktopState(bool desktopShown)
    {
        ShowDesktopButton.IsChecked = desktopShown;
        var label = desktopShown ? "Restore windows" : "Show desktop";
        AutomationProperties.SetName(ShowDesktopButton, label);
        AutomationProperties.SetHelpText(
            ShowDesktopButton,
            desktopShown
                ? "Restores the windows minimized by Show desktop."
                : "Minimizes every application window. Select again to restore them.");
        ToolTipService.SetToolTip(ShowDesktopButton, label);
        ShowDesktopIcon.Visibility =
            desktopShown ? Visibility.Collapsed : Visibility.Visible;
        RestoreWindowsIcon.Visibility =
            desktopShown ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Shutdown()
    {
        _autoHideTimer.Stop();
        _displayScaleRefreshTimer.Stop();
        _previewDelayTimer.Stop();
        _previewDismissTimer.Stop();
        StopVisibilityTransition();
        foreach (var transition in _launchFeedbackTransitions.Values)
        {
            transition.Stop();
        }

        _launchFeedbackTransitions.Clear();
        _previewWindow?.Shutdown();
        _previewWindow = null;
        DismissDockMagnifier();
        _iconMagnifierWindow?.Dispose();
        _iconMagnifierWindow = null;
        if (_windowListScrollViewer is not null)
        {
            _windowListScrollViewer.ViewChanged -= OnWindowListViewChanged;
            _windowListScrollViewer = null;
        }
        _shellState.StateChanged -= OnShellStateChanged;
        _ = _workAreaReservation.Release();
        _allowClose = true;
        Close();
    }

    private void ConfigurePresenter()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
    }

    private void SetCollapsed(bool collapsed)
    {
        var stateChanged = _collapsed != collapsed;
        if (collapsed)
        {
            DismissWindowPreview();
            DismissDockMagnifier();
        }

        _collapsed = collapsed;
        if (!stateChanged ||
            _reducedEffects ||
            !AppWindow.IsVisible)
        {
            StopVisibilityTransition();
            ApplyDockLayout(collapsed);
            return;
        }

        if (collapsed)
        {
            BeginVisibilityTransition(collapsed: true);
            return;
        }

        ApplyDockLayout(collapsed: false);
        BeginVisibilityTransition(collapsed: false);
    }

    private void ApplyDockLayout(bool collapsed)
    {
        ExpandedDock.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        PeekIndicator.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;

        var placementMonitor = _reservedArea is null
            ? _monitor
            : _monitor with
            {
                WorkAreaX = _reservedArea.X,
                WorkAreaY = _reservedArea.Y,
                WorkAreaWidth = _reservedArea.Width,
                WorkAreaHeight = _reservedArea.Height,
            };
        var bounds = DockPlacement.Calculate(
            placementMonitor,
            _expandedDockWidth,
            ToPhysicalPixels(
                AccessibilityLayout.ScaleDockHeight(
                    _compactDensity ? CompactDockHeight : DockHeight,
                    _textScaleFactor)),
            collapsed,
            ToPhysicalPixels(PeekWidth),
            ToPhysicalPixels(PeekHeight));
        AppWindow.Resize(new SizeInt32(bounds.Width, bounds.Height));
        AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = DwmWindowChrome.TryConfigureFloatingSurface(windowHandle);
        _ = DwmWindowChrome.TryApplyRoundedClip(
            windowHandle,
            bounds.Width,
            bounds.Height,
            ToPhysicalPixels(collapsed ? 5 : 24),
            ToPhysicalPixels(2));
    }

    private void BeginVisibilityTransition(bool collapsed)
    {
        StopVisibilityTransition();
        var motion = DockVisibilityMotion.Resolve(
            collapsed,
            reducedEffects: false);
        var translate = (TranslateTransform)ExpandedDock.RenderTransform;
        ExpandedDock.Visibility = Visibility.Visible;
        PeekIndicator.Visibility = Visibility.Collapsed;
        ExpandedDock.Opacity = motion.StartOpacity;
        translate.Y = motion.StartTranslationY;

        var easing = new CubicEase
        {
            EasingMode = collapsed
                ? EasingMode.EaseIn
                : EasingMode.EaseOut,
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
        Storyboard.SetTarget(opacity, ExpandedDock);
        Storyboard.SetTargetProperty(opacity, nameof(UIElement.Opacity));
        var translation = new DoubleAnimation
        {
            From = motion.StartTranslationY,
            To = motion.EndTranslationY,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(translation, translate);
        Storyboard.SetTargetProperty(translation, nameof(TranslateTransform.Y));

        var transition = new Storyboard();
        transition.Children.Add(opacity);
        transition.Children.Add(translation);
        transition.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_visibilityTransition, transition))
            {
                return;
            }

            ExpandedDock.Opacity = motion.EndOpacity;
            translate.Y = motion.EndTranslationY;
            _visibilityTransition = null;
            transition.Stop();
            if (collapsed && _collapsed)
            {
                ApplyDockLayout(collapsed: true);
            }
        };
        _visibilityTransition = transition;
        transition.Begin();
    }

    private void StopVisibilityTransition()
    {
        _visibilityTransition?.Stop();
        _visibilityTransition = null;
        ExpandedDock.Opacity = 1;
        if (ExpandedDock.RenderTransform is TranslateTransform translate)
        {
            translate.Y = 0;
        }
    }

    private void ScheduleAutoHide()
    {
        if ((!_autoHide && !_immersiveSuppressed) ||
            _contextMenuOpen ||
            _modalDialogOpen ||
            _previewWindow?.IsVisible == true ||
            _shellState.Current.Mode == ShellMode.Gaming)
        {
            return;
        }

        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    public void ApplyWindowSnapshot(IReadOnlyList<DesktopWindowSnapshot> snapshot)
    {
        if (_allowClose)
        {
            return;
        }

        var windows = DockForegroundContinuity.Apply(
            DesktopWindowFilter.ForMonitor(snapshot, _monitor.Handle),
            _returnFocusWindow);
        if (windows.Count == _monitorWindows.Count && windows
            .Select(static window => (
                window.Handle,
                window.Title,
                window.ProcessName,
                window.IsMinimized,
                window.IsForeground))
            .SequenceEqual(_monitorWindows.Select(static window => (
                window.Handle,
                window.Title,
                window.ProcessName,
                window.IsMinimized,
                window.IsForeground))))
        {
            return;
        }

        _monitorWindows = windows;
        DismissWindowPreview();
        DismissDockMagnifier();
        RefreshWindowItems();
        DockCountText.Text = windows.Count == 1 ? "1 window" : $"{windows.Count} windows";
        ApplyEmptyState(DockEmptyStatePresentation.NoWindows(_monitor.DeviceName));
        EmptyState.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshPinnedItems();
    }

    private void RefreshWindowItems()
    {
        Items.Clear();
        var orderedGroups = TaskbarWindowOrder.Apply(
            TaskbarWindowGrouper.Group(_monitorWindows),
            _windowGroupOrder);
        _windowGroupOrder = orderedGroups.Keys;
        foreach (var group in orderedGroups.Groups)
        {
            var isPinned = TaskbarDockPinResolver.FindPinnedApplication(
                _pinnedApplications,
                group.Windows) is not null;
            var item = new DockItemViewModel(group, isPinned);
            Items.Add(item);
            _ = item.LoadIconAsync();
        }

        WindowList.SelectedItem = Items.FirstOrDefault(static item => item.IsForeground);
        ResetWindowOverflowControls();
    }

    private void OnWindowListLoaded(object sender, RoutedEventArgs e)
    {
        if (_windowListScrollViewer is null)
        {
            _windowListScrollViewer =
                FindDescendant<ScrollViewer>(WindowList);
            if (_windowListScrollViewer is not null)
            {
                _windowListScrollViewer.ViewChanged += OnWindowListViewChanged;
            }
        }

        QueueWindowOverflowRefresh();
    }

    private void OnWindowListSizeChanged(object sender, SizeChangedEventArgs e) =>
        QueueWindowOverflowRefresh();

    private void OnWindowListViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e) =>
        RefreshWindowOverflowControls();

    private void OnWindowOverflowPreviousClicked(
        object sender,
        RoutedEventArgs e) =>
        NavigateWindowOverflow(DockOverflowDirection.Previous);

    private void OnWindowOverflowNextClicked(
        object sender,
        RoutedEventArgs e) =>
        NavigateWindowOverflow(DockOverflowDirection.Next);

    private void OnWindowListPointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(WindowList).Properties.MouseWheelDelta;
        if (_windowListScrollViewer is null ||
            _windowListScrollViewer.ScrollableWidth <= 0 ||
            delta == 0)
        {
            return;
        }

        NavigateWindowOverflow(
            delta > 0
                ? DockOverflowDirection.Previous
                : DockOverflowDirection.Next);
        e.Handled = true;
    }

    private void NavigateWindowOverflow(DockOverflowDirection direction)
    {
        if (_windowListScrollViewer is null)
        {
            return;
        }

        var targetOffset = DockOverflowNavigation.CalculateTargetOffset(
            _windowListScrollViewer.HorizontalOffset,
            _windowListScrollViewer.ViewportWidth,
            _windowListScrollViewer.ScrollableWidth,
            direction);
        _ = _windowListScrollViewer.ChangeView(
            targetOffset,
            null,
            null,
            disableAnimation: _reducedEffects);
    }

    private void ResetWindowOverflowControls()
    {
        WindowOverflowPreviousButton.Visibility = Visibility.Collapsed;
        WindowOverflowNextButton.Visibility = Visibility.Collapsed;
        QueueWindowOverflowRefresh();
    }

    private void QueueWindowOverflowRefresh() =>
        _ = DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            RefreshWindowOverflowControls);

    private void RefreshWindowOverflowControls()
    {
        if (_windowListScrollViewer is null)
        {
            return;
        }

        var state = DockOverflowNavigation.Resolve(
            _windowListScrollViewer.HorizontalOffset,
            _windowListScrollViewer.ScrollableWidth);
        var visibility = state.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        WindowOverflowPreviousButton.Visibility = visibility;
        WindowOverflowPreviousButton.IsEnabled = state.CanNavigatePrevious;
        WindowOverflowNextButton.Visibility = visibility;
        WindowOverflowNextButton.IsEnabled = state.CanNavigateNext;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    public void ApplyPinnedApplications(
        IReadOnlyList<ShellCommand> applications)
    {
        if (_allowClose ||
            applications.Select(static command => command.Id).SequenceEqual(
                _pinnedApplications.Select(static command => command.Id),
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _pinnedApplications = applications.ToArray();
        RefreshWindowItems();
        RefreshPinnedItems();
    }

    public void ApplyAvailableApplications(
        IReadOnlyList<ShellCommand> applications)
    {
        if (_allowClose)
        {
            return;
        }

        _availableApplications = applications.ToArray();
    }

    public void SetWindowSnapshotUnavailable(string message)
    {
        if (_allowClose)
        {
            return;
        }

        _monitorWindows = [];
        Items.Clear();
        WindowList.SelectedItem = null;
        DockCountText.Text = "Unavailable";
        ApplyEmptyState(DockEmptyStatePresentation.Unavailable(message));
        EmptyState.Visibility = Visibility.Visible;
        RefreshPinnedItems();
    }

    private void ApplyEmptyState(DockEmptyStateState state)
    {
        EmptyStateIcon.Glyph = state.Glyph;
        EmptyStateIcon.Foreground = GetThemeBrush(
            state.IsError
                ? "SystemFillColorCriticalBrush"
                : "TextFillColorSecondaryBrush");
        EmptyStateToolTipText.Text = state.Description;
        AutomationProperties.SetName(EmptyState, state.Description);
        AutomationProperties.SetHelpText(EmptyState, state.Description);
    }

    private void OnAutoHideTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_pointerInside && !_hasKeyboardFocus && _autoHide)
        {
            SetCollapsed(true);
        }
    }

    private void RefreshExpandedWidth()
    {
        var expandedDockWidth = CalculateExpandedWidth(
            PinnedItems.Count,
            Items.Count);
        if (_expandedDockWidth == expandedDockWidth)
        {
            return;
        }

        _expandedDockWidth = expandedDockWidth;
        SetCollapsed(_collapsed);
    }

    private int CalculateExpandedWidth(int pinnedItemCount, int windowItemCount)
    {
        var logicalWorkAreaWidth =
            DisplayScaleLayout.ToDeviceIndependentPixels(
                _monitor.WorkAreaWidth,
                _displayScaleFactor);
        var logicalDockWidth = TaskbarDockLayout.CalculateExpandedWidth(
            pinnedItemCount,
            windowItemCount,
            logicalWorkAreaWidth,
            _textScaleFactor);
        return ToPhysicalPixels(logicalDockWidth);
    }

    private int ToPhysicalPixels(int deviceIndependentPixels) =>
        DisplayScaleLayout.ToPhysicalPixels(
            deviceIndependentPixels,
            _displayScaleFactor);

    private void RefreshWindowScaleFactor()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var displayScaleFactor =
            DisplayDpiService.GetWindowScaleFactor(windowHandle);
        if (Math.Abs(_displayScaleFactor - displayScaleFactor) < 0.001)
        {
            return;
        }

        _displayScaleFactor = displayScaleFactor;
        _expandedDockWidth = CalculateExpandedWidth(
            PinnedItems.Count,
            Items.Count);
        SetCollapsed(_collapsed);
    }

    private void OnExpandedDockLoaded(object sender, RoutedEventArgs e)
    {
        ExpandedDock.Loaded -= OnExpandedDockLoaded;
        _ = DwmWindowChrome.TryConfigureFloatingSurface(
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        ScheduleDisplayScaleRefresh();
    }

    private void ScheduleDisplayScaleRefresh()
    {
        _displayScaleRefreshTimer.Stop();
        _displayScaleRefreshTimer.Start();
    }

    private void OnDisplayScaleRefreshTimerTick(
        DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        RefreshWindowScaleFactor();
    }

    private void RefreshPinnedItems()
    {
        var visibleApplications = _pinnedApplications
            .Where(application => !_monitorWindows.Any(
                window => TaskbarPinWindowMatcher.IsMatch(application, window)))
            .ToArray();
        if (!visibleApplications
            .Select(static command => command.Id)
            .SequenceEqual(
                PinnedItems.Select(static item => item.Id),
                StringComparer.OrdinalIgnoreCase))
        {
            PinnedItems.Clear();
            foreach (var application in visibleApplications)
            {
                var item = new PinnedDockItemViewModel(application);
                PinnedItems.Add(item);
                _ = item.LoadIconAsync();
            }
        }

        PinnedSeparator.Visibility =
            PinnedItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RefreshExpandedWidth();
    }

    private void OnDockPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        _autoHideTimer.Stop();
        if (_collapsed)
        {
            SetCollapsed(false);
        }
    }

    private void OnDockPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        ScheduleAutoHide();
    }

    private void OnDockItemPointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ResetDockNeighborMotion();
            ApplyDockItemMotion(element, isPointerOver: true, isPressed: false);
            ApplyDockNeighborMotion(element);
            ShowDockMagnifier(element);
            if (element.DataContext is DockItemViewModel item)
            {
                ScheduleWindowPreview(item, element);
            }
        }
    }

    private void OnDockItemPointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (ReferenceEquals(element, _magnifiedElement))
            {
                DismissDockMagnifier();
            }

            ResetDockNeighborMotion();
            ApplyDockItemMotion(element, isPointerOver: false, isPressed: false);
            if (element.DataContext is DockItemViewModel)
            {
                _previewDelayTimer.Stop();
                _pendingPreviewItem = null;
                _pendingPreviewAnchor = null;
                ScheduleWindowPreviewDismissal();
            }
        }
    }

    private void ScheduleWindowPreview(
        DockItemViewModel item,
        FrameworkElement anchor)
    {
        _previewDismissTimer.Stop();
        _pendingPreviewItem = item;
        _pendingPreviewAnchor = anchor;
        _previewDelayTimer.Stop();
        _previewDelayTimer.Start();
    }

    private void ScheduleWindowPreviewDismissal()
    {
        _previewDismissTimer.Stop();
        _previewDismissTimer.Start();
    }

    private void OnPreviewDelayTimerTick(
        DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        var item = _pendingPreviewItem;
        var anchor = _pendingPreviewAnchor;
        _pendingPreviewItem = null;
        _pendingPreviewAnchor = null;
        if (item is null ||
            anchor is null ||
            _collapsed ||
            !anchor.IsLoaded)
        {
            return;
        }

        _previewWindow ??= CreateWindowPreview();
        var point = anchor
            .TransformToVisual(DockRoot)
            .TransformPoint(new global::Windows.Foundation.Point(0, 0));
        var scale = DockRoot.XamlRoot?.RasterizationScale ?? 1;
        var anchorCenterX =
            AppWindow.Position.X +
            (int)Math.Round((point.X + (anchor.ActualWidth / 2)) * scale);
        _autoHideTimer.Stop();
        _previewWindow.Show(
            item,
            anchorCenterX,
            AppWindow.Position.Y,
            _monitor,
            scale);
    }

    private WindowPreviewWindow CreateWindowPreview()
    {
        var preview = new WindowPreviewWindow(_windowService);
        preview.PreviewEntered += (_, _) =>
        {
            _previewDismissTimer.Stop();
            _autoHideTimer.Stop();
        };
        preview.PreviewExited += (_, _) =>
            ScheduleWindowPreviewDismissal();
        preview.Dismissed += (_, _) =>
            ScheduleAutoHide();
        return preview;
    }

    private void OnPreviewDismissTimerTick(
        DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        DismissWindowPreview();
    }

    private void DismissWindowPreview()
    {
        _previewDelayTimer.Stop();
        _previewDismissTimer.Stop();
        _pendingPreviewItem = null;
        _pendingPreviewAnchor = null;
        _previewWindow?.Dismiss();
    }

    private async void OnDockItemPointerPressed(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var properties = e.GetCurrentPoint(element).Properties;
        if (properties.IsMiddleButtonPressed)
        {
            e.Handled = true;
            await HandleMiddleClickAsync(element).ConfigureAwait(true);
            ScheduleAutoHide();
            return;
        }

        if (properties.IsLeftButtonPressed)
        {
            ApplyDockItemMotion(element, isPointerOver: true, isPressed: true);
            if (ReferenceEquals(element, _magnifiedElement))
            {
                _iconMagnifierWindow?.SetPressed(
                    pressed: true,
                    DockRoot.XamlRoot?.RasterizationScale ?? 1);
            }
        }
    }

    private async Task HandleMiddleClickAsync(FrameworkElement anchor)
    {
        if (anchor.DataContext is PinnedDockItemViewModel pinnedItem)
        {
            PlayLaunchFeedback(pinnedItem);
            await OpenNewInstanceAsync(pinnedItem.Command).ConfigureAwait(true);
            return;
        }

        if (anchor.DataContext is not DockItemViewModel dockItem)
        {
            return;
        }

        await OpenNewInstanceForDockItemAsync(
                dockItem,
                anchor,
                elevated: false)
            .ConfigureAwait(true);
    }

    private async Task OpenNewInstanceForDockItemAsync(
        DockItemViewModel dockItem,
        FrameworkElement anchor,
        bool elevated)
    {
        IReadOnlyList<ShellCommand> candidates =
            TaskbarDockPinResolver.FindApplicationCandidates(
            _availableApplications,
            dockItem.Windows);
        if (elevated)
        {
            candidates = candidates
                .Where(candidate =>
                    IsVerifiedLocalExecutable(
                        candidate.ApplicationExecutablePath))
                .ToArray();
        }

        switch (TaskbarMiddleClickResolver.Resolve(candidates.Count))
        {
            case TaskbarMiddleClickAction.Open:
                PlayLaunchFeedback(dockItem);
                await OpenApplicationInstanceAsync(candidates[0], elevated)
                    .ConfigureAwait(true);
                break;
            case TaskbarMiddleClickAction.Choose:
                PlayLaunchFeedback(dockItem);
                ShowOpenNewInstancePicker(candidates, anchor, elevated);
                break;
            case TaskbarMiddleClickAction.None:
                dockItem.SetInteractionNotice(
                    elevated
                        ? "Elevated launch unavailable • No verified local executable"
                        : "New instance unavailable • No matching installed application");
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported taskbar middle-click action.");
        }
    }

    private void OnDockItemPointerReleased(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyDockItemMotion(element, isPointerOver: true, isPressed: false);
            if (ReferenceEquals(element, _magnifiedElement))
            {
                _iconMagnifierWindow?.SetPressed(
                    pressed: false,
                    DockRoot.XamlRoot?.RasterizationScale ?? 1);
            }
        }
    }

    private void ShowDockMagnifier(FrameworkElement element)
    {
        if (_reducedEffects || _collapsed)
        {
            return;
        }

        ApplicationIconSnapshot? snapshot;
        IReadOnlyList<FrameworkElement> iconVisuals;
        var isRunning = false;
        var isActive = false;
        var isMinimized = false;
        if (ReferenceEquals(element, LauncherItem))
        {
            snapshot = LauncherMagnifierIcon.Value;
            iconVisuals = [LauncherButton, LauncherIcon];
        }
        else if (element.DataContext is PinnedDockItemViewModel pinnedItem)
        {
            snapshot = pinnedItem.Icon is null ? null : pinnedItem.Command.Icon;
            iconVisuals = FindIconVisuals(element);
        }
        else if (element.DataContext is DockItemViewModel dockItem)
        {
            snapshot = dockItem.Icon is null ? null : dockItem.IconSnapshot;
            iconVisuals = FindIconVisuals(element);
            isRunning = true;
            isActive = dockItem.IsForeground;
            isMinimized = dockItem.IsMinimized;
        }
        else
        {
            return;
        }

        if (snapshot is null || iconVisuals.Count == 0)
        {
            return;
        }

        DismissDockMagnifier();
        var point = element
            .TransformToVisual(DockRoot)
            .TransformPoint(new Point(0, 0));
        var scale = DockRoot.XamlRoot?.RasterizationScale ?? 1;
        var anchorCenterX = AppWindow.Position.X + (int)Math.Round(
            (point.X + (element.ActualWidth / 2)) * scale);
        var anchorBottomY = AppWindow.Position.Y + (int)Math.Round(
            (point.Y + element.ActualHeight) * scale);
        _iconMagnifierWindow ??= new LayeredDockIconWindow();
        if (!_iconMagnifierWindow.Show(
            snapshot,
            isRunning,
            isActive,
            isMinimized,
            anchorCenterX,
            anchorBottomY,
            _monitor,
            scale))
        {
            return;
        }

        foreach (var iconVisual in iconVisuals)
        {
            iconVisual.Opacity = 0;
        }

        _magnifiedElement = element;
        _magnifiedIconVisuals = iconVisuals;
    }

    private void DismissDockMagnifier()
    {
        foreach (var iconVisual in _magnifiedIconVisuals)
        {
            iconVisual.Opacity = 1;
        }

        _magnifiedElement = null;
        _magnifiedIconVisuals = [];
        _iconMagnifierWindow?.Dismiss();
    }

    private static IReadOnlyList<FrameworkElement> FindIconVisuals(
        DependencyObject root)
    {
        var icons = new List<FrameworkElement>();
        CollectIconVisuals(root, icons);
        return icons;
    }

    private static void CollectIconVisuals(
        DependencyObject parent,
        ICollection<FrameworkElement> icons)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Image image)
            {
                icons.Add(image);
            }
            else if (child is FrameworkElement element &&
                string.Equals(
                    element.Tag as string,
                    "DockIconOverlayVisual",
                    StringComparison.Ordinal))
            {
                icons.Add(element);
            }

            CollectIconVisuals(child, icons);
        }
    }

    private void ApplyDockItemMotion(
        FrameworkElement element,
        bool isPointerOver,
        bool isPressed)
    {
        var usesApplicationMotion = ReferenceEquals(element, LauncherItem)
            || element.DataContext is DockItemViewModel
            || element.DataContext is PinnedDockItemViewModel;
        var motionTarget = ReferenceEquals(element, LauncherItem)
            ? LauncherIcon
            : element;
        var motion = usesApplicationMotion
            ? DockItemMotion.Resolve(isPointerOver, isPressed, _reducedEffects)
            : DockControlMotion.Resolve(isPointerOver, isPressed, _reducedEffects);
        ApplyDockMotion(
            element,
            motionTarget,
            motion,
            usesApplicationMotion,
            isPointerOver ? 10 : 0);
    }

    private void OnDockMotionItemLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            _dockMotionItems.Add(element);
        }
    }

    private void OnDockMotionItemUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (ReferenceEquals(element, _magnifiedElement))
            {
                DismissDockMagnifier();
            }

            _dockMotionItems.Remove(element);
            _activeDockNeighborItems.Remove(element);
            if (_launchFeedbackTransitions.Remove(element, out var transition))
            {
                transition.Stop();
            }
        }
    }

    private void ApplyDockNeighborMotion(FrameworkElement source)
    {
        if (_reducedEffects || !_dockMotionItems.Contains(source))
        {
            return;
        }

        var orderedItems = _dockMotionItems
            .Where(item => item.IsLoaded && item.ActualWidth > 0)
            .Select(item => new
            {
                Item = item,
                CenterX = GetDockMotionItemCenterX(item),
            })
            .Where(position => position.CenterX >= 0
                && position.CenterX <= DockRoot.ActualWidth)
            .OrderBy(position => position.CenterX)
            .Select(position => position.Item)
            .ToList();
        var sourceIndex = orderedItems.IndexOf(source);
        if (sourceIndex < 0)
        {
            return;
        }

        foreach (var neighborIndex in new[] { sourceIndex - 1, sourceIndex + 1 })
        {
            if (neighborIndex < 0 || neighborIndex >= orderedItems.Count)
            {
                continue;
            }

            var neighbor = orderedItems[neighborIndex];
            var motionTarget = ReferenceEquals(neighbor, LauncherItem)
                ? LauncherIcon
                : neighbor;
            ApplyDockMotion(
                neighbor,
                motionTarget,
                DockItemMotion.ResolveNeighbor(
                    isHighlighted: true,
                    reducedEffects: _reducedEffects),
                bottomAnchored: true,
                zIndex: 5);
            _activeDockNeighborItems.Add(neighbor);
        }
    }

    private void ResetDockNeighborMotion()
    {
        foreach (var neighbor in _activeDockNeighborItems)
        {
            var motionTarget = ReferenceEquals(neighbor, LauncherItem)
                ? LauncherIcon
                : neighbor;
            ApplyDockMotion(
                neighbor,
                motionTarget,
                DockItemMotion.ResolveNeighbor(
                    isHighlighted: false,
                    reducedEffects: _reducedEffects),
                bottomAnchored: true,
                zIndex: 0);
        }

        _activeDockNeighborItems.Clear();
    }

    private double GetDockMotionItemCenterX(FrameworkElement item)
    {
        try
        {
            return item.TransformToVisual(DockRoot).TransformPoint(
                new Point(item.ActualWidth / 2, item.ActualHeight / 2)).X;
        }
        catch (InvalidOperationException)
        {
            return double.MaxValue;
        }
    }

    private static void ApplyDockMotion(
        FrameworkElement source,
        FrameworkElement motionTarget,
        DockItemMotionState motion,
        bool bottomAnchored,
        int zIndex)
    {
        motionTarget.CenterPoint = new Vector3(
            (float)(motionTarget.ActualWidth / 2),
            bottomAnchored
                ? (float)motionTarget.ActualHeight
                : (float)(motionTarget.ActualHeight / 2),
            0);
        Canvas.SetZIndex(source, zIndex);
        Canvas.SetZIndex(motionTarget, zIndex);
        var duration = TimeSpan.FromMilliseconds(motion.DurationMilliseconds);
        motionTarget.ScaleTransition = motion.DurationMilliseconds == 0
            ? null
            : new Vector3Transition { Duration = duration };
        motionTarget.TranslationTransition = motion.DurationMilliseconds == 0
            ? null
            : new Vector3Transition { Duration = duration };
        motionTarget.Scale = new Vector3(motion.Scale, motion.Scale, 1);
        motionTarget.Translation = new Vector3(0, motion.TranslationY, 0);
    }

    private void OnDockGotFocus(object sender, RoutedEventArgs e)
    {
        _hasKeyboardFocus = true;
        _autoHideTimer.Stop();
        if (_collapsed)
        {
            SetCollapsed(false);
        }
    }

    private void OnDockLostFocus(object sender, RoutedEventArgs e)
    {
        _hasKeyboardFocus = false;
        ScheduleAutoHide();
    }

    private void OnDockKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != global::Windows.System.VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        var returnFocusWindow = _returnFocusWindow;
        _returnFocusWindow = 0;
        if (returnFocusWindow != 0)
        {
            _ = _windowService.RestoreAndActivate(returnFocusWindow);
        }

        if (_autoHide)
        {
            _autoHideTimer.Stop();
            SetCollapsed(true);
        }
    }

    private async void OnWindowClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DockItemViewModel item)
        {
            return;
        }

        DismissWindowPreview();
        var clickAction = TaskbarClickActionResolver.Resolve(
            KeyboardModifierStateReader.IsShiftPressed(),
            KeyboardModifierStateReader.IsControlPressed());
        if (clickAction != TaskbarClickAction.Default)
        {
            if (clickAction == TaskbarClickAction.CycleWindows)
            {
                var targetIndex = TaskbarWindowCycleResolver.ResolveNextIndex(
                    item.Windows
                        .Select(static window => window.IsForeground)
                        .ToArray());
                if (targetIndex >= 0)
                {
                    _ = _windowService.RestoreAndActivate(
                        item.Windows[targetIndex].Handle);
                }

                ScheduleAutoHide();
                return;
            }

            if (WindowList.ContainerFromItem(item) is FrameworkElement shiftAnchor)
            {
                await OpenNewInstanceForDockItemAsync(
                        item,
                        shiftAnchor,
                        elevated: clickAction ==
                            TaskbarClickAction.OpenElevatedInstance)
                    .ConfigureAwait(true);
            }

            ScheduleAutoHide();
            return;
        }

        if (item.WindowCount == 1)
        {
            _ = _windowService.ToggleTaskbarWindow(item.PrimaryWindow.Handle);
            ScheduleAutoHide();
            return;
        }

        if (WindowList.ContainerFromItem(item) is FrameworkElement anchor)
        {
            ShowWindowPicker(item, anchor);
        }
    }

    private void OnWindowContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not DockItemViewModel item)
        {
            return;
        }

        DismissWindowPreview();
        if (item.WindowCount > 1)
        {
            ShowGroupWindowContextMenu(item, element);
            args.Handled = true;
            return;
        }

        var window = item.PrimaryWindow;
        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var toggleAction =
            TaskbarWindowActionResolver.ResolveContextToggle(window.IsMinimized);
        var activateItem = new MenuFlyoutItem
        {
            Text = "Activate",
            Icon = CreateMenuIcon("\uE8A7"),
        };
        activateItem.Click += (_, _) =>
            _ = _windowService.RestoreAndActivate(window.Handle);

        var toggleItem = new MenuFlyoutItem
        {
            Text = toggleAction == TaskbarWindowAction.Minimize
                ? "Minimize"
                : "Restore",
            Icon = CreateMenuIcon(
                toggleAction == TaskbarWindowAction.Minimize
                    ? "\uE921"
                    : "\uE923"),
        };
        toggleItem.Click += (_, _) =>
        {
            _ = toggleAction == TaskbarWindowAction.Minimize
                ? _windowService.Minimize(window.Handle)
                : _windowService.RestoreAndActivate(window.Handle);
        };

        var closeItem = new MenuFlyoutItem
        {
            Text = "Close window",
            Icon = CreateMenuIcon("\uE8BB"),
        };
        closeItem.Click += (_, _) => _ = _windowService.RequestClose(window.Handle);

        var flyout = CreateDockMenuFlyout();
        flyout.Items.Add(activateItem);
        flyout.Items.Add(toggleItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(closeItem);
        AddRunningOrderActions(flyout.Items, item);
        AddOpenNewInstanceAction(flyout.Items, item);
        AddRunningExecutableActions(flyout.Items, item);
        AddPinAction(flyout.Items, item);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(element);
        args.Handled = true;
    }

    private void ShowGroupWindowContextMenu(
        DockItemViewModel item,
        FrameworkElement anchor)
    {
        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var flyout = CreateDockMenuFlyout();
        for (var index = 0; index < item.Windows.Count; index++)
        {
            var window = item.Windows[index];
            var windowMenu = new MenuFlyoutSubItem
            {
                Text = GetWindowMenuText(item, index),
                Icon = CreateWindowStateIcon(window),
            };

            var activateItem = new MenuFlyoutItem
            {
                Text = "Activate",
                Icon = CreateMenuIcon("\uE8A7"),
            };
            activateItem.Click += (_, _) =>
                _ = _windowService.RestoreAndActivate(window.Handle);
            windowMenu.Items.Add(activateItem);

            var toggleAction =
                TaskbarWindowActionResolver.ResolveContextToggle(window.IsMinimized);
            var toggleItem = new MenuFlyoutItem
            {
                Text = toggleAction == TaskbarWindowAction.Minimize
                    ? "Minimize"
                    : "Restore",
                Icon = CreateMenuIcon(
                    toggleAction == TaskbarWindowAction.Minimize
                        ? "\uE921"
                        : "\uE923"),
            };
            toggleItem.Click += (_, _) =>
            {
                _ = toggleAction == TaskbarWindowAction.Minimize
                    ? _windowService.Minimize(window.Handle)
                    : _windowService.RestoreAndActivate(window.Handle);
            };
            windowMenu.Items.Add(toggleItem);
            windowMenu.Items.Add(new MenuFlyoutSeparator());

            var closeItem = new MenuFlyoutItem
            {
                Text = "Close window",
                Icon = CreateMenuIcon("\uE8BB"),
            };
            closeItem.Click += (_, _) =>
                _ = _windowService.RequestClose(window.Handle);
            windowMenu.Items.Add(closeItem);
            flyout.Items.Add(windowMenu);
        }

        var windowHandles = item.Windows
            .Select(static window => window.Handle)
            .Distinct()
            .ToArray();
        var groupAction = TaskbarWindowGroupActionResolver.Resolve(
            item.Windows
                .Select(static window => window.IsMinimized)
                .ToArray());
        var toggleGroupItem = new MenuFlyoutItem
        {
            Text = groupAction == TaskbarWindowGroupAction.RestoreAll
                ? "Restore all windows"
                : "Minimize all windows",
            Icon = CreateMenuIcon(
                groupAction == TaskbarWindowGroupAction.RestoreAll
                    ? "\uE923"
                    : "\uE921"),
        };
        toggleGroupItem.Click += (_, _) =>
        {
            foreach (var handle in windowHandles)
            {
                _ = groupAction == TaskbarWindowGroupAction.RestoreAll
                    ? _windowService.Restore(handle)
                    : _windowService.Minimize(handle);
            }
        };
        var closeAllItem = new MenuFlyoutItem
        {
            Text = $"Close all windows ({windowHandles.Length})",
            Icon = CreateMenuIcon("\uE8BB"),
        };
        closeAllItem.Click += (_, _) =>
        {
            foreach (var handle in windowHandles)
            {
                _ = _windowService.RequestClose(handle);
            }
        };
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(toggleGroupItem);
        flyout.Items.Add(closeAllItem);

        AddRunningOrderActions(flyout.Items, item);
        AddOpenNewInstanceAction(flyout.Items, item);
        AddRunningExecutableActions(flyout.Items, item);
        AddPinAction(flyout.Items, item);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(anchor);
    }

    private void ShowWindowPicker(
        DockItemViewModel item,
        FrameworkElement anchor)
    {
        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var flyout = CreateDockMenuFlyout();
        for (var index = 0; index < item.Windows.Count; index++)
        {
            var window = item.Windows[index];
            var windowItem = new MenuFlyoutItem
            {
                Text = GetWindowMenuText(item, index),
                Icon = CreateWindowStateIcon(window),
            };
            windowItem.Click += (_, _) =>
                _ = _windowService.RestoreAndActivate(window.Handle);
            flyout.Items.Add(windowItem);
        }

        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(anchor);
    }

    private static MenuFlyout CreateDockMenuFlyout()
    {
        var flyout = new MenuFlyout();
        if (Application.Current.Resources.TryGetValue(
                "SeanDockMenuFlyoutPresenterStyle",
                out var style) &&
            style is Style presenterStyle)
        {
            flyout.MenuFlyoutPresenterStyle = presenterStyle;
        }

        return flyout;
    }

    private static string GetWindowMenuText(
        DockItemViewModel item,
        int index) =>
        $"{GetWindowDisplayTitle(item, index)} — {GetWindowStateText(item.Windows[index])}";

    private static string GetWindowStateText(DesktopWindowSnapshot window) =>
        window.IsForeground
            ? "Active"
            : window.IsMinimized
                ? "Minimized"
                : "Running";

    private static string GetWindowDisplayTitle(
        DockItemViewModel item,
        int index)
    {
        var window = item.Windows[index];
        var matchingTitleCount = item.Windows.Count(candidate =>
            candidate.Title.Equals(
                window.Title,
                StringComparison.CurrentCultureIgnoreCase));
        if (matchingTitleCount == 1)
        {
            return window.Title;
        }

        var matchingTitleIndex = item.Windows
            .Take(index + 1)
            .Count(candidate => candidate.Title.Equals(
                window.Title,
                StringComparison.CurrentCultureIgnoreCase));
        return $"{window.Title} ({matchingTitleIndex} of {matchingTitleCount})";
    }

    private static FontIcon CreateWindowStateIcon(
        DesktopWindowSnapshot window) =>
        CreateMenuIcon(window.IsForeground ? "\uE73E" : "\uE8A7");

    private static FontIcon CreateMenuIcon(string glyph) =>
        new()
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                "Segoe Fluent Icons"),
            FontSize = 16,
            Glyph = glyph,
        };

    private void OnLauncherClicked(object sender, RoutedEventArgs e)
    {
        PlayLaunchFeedback(LauncherIcon);
        LauncherRequested?.Invoke(this, EventArgs.Empty);
        ScheduleAutoHide();
    }

    private void OnLauncherContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement anchor)
        {
            return;
        }

        _autoHideTimer.Stop();
        _contextMenuOpen = true;
        var flyout = CreateDockMenuFlyout();
        var dashboardItem = new MenuFlyoutItem
        {
            Text = "Open Dashboard",
            Icon = CreateMenuIcon("\uE80F"),
        };
        dashboardItem.Click += (_, _) =>
            DashboardRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(dashboardItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddSystemTool(flyout, "File Explorer", "\uEC50", "explorer.exe");
        AddSystemTool(flyout, "Windows Terminal", "\uE756", "wt.exe");
        AddSystemTool(flyout, "Task Manager", "\uE9D9", "taskmgr.exe");
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddSystemTool(flyout, "Windows Settings", "\uE713", "ms-settings:");
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddPowerMenu(flyout);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(anchor);
        args.Handled = true;
    }

    private void OnDockContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement anchor)
        {
            return;
        }

        _autoHideTimer.Stop();
        _contextMenuOpen = true;
        var flyout = CreateDockMenuFlyout();

        var settingsItem = new MenuFlyoutItem
        {
            Text = "SeanShell settings",
            Icon = CreateMenuIcon("\uE713"),
        };
        settingsItem.Click += (_, _) =>
            DashboardRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(settingsItem);

        AddSystemTool(flyout, "Task Manager", "\uE9D9", "taskmgr.exe");
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddSystemTool(
            flyout,
            "Windows taskbar settings",
            "\uE713",
            "ms-settings:taskbar");
        AddSystemTool(flyout, "Windows Settings", "\uE713", "ms-settings:");

        if (SystemAreaButton.Visibility == Visibility.Visible)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            var systemAreaItem = new MenuFlyoutItem
            {
                Text = AutomationProperties.GetName(SystemAreaButton),
                Icon = CreateMenuIcon("\uE7F4"),
            };
            systemAreaItem.Click += (_, _) =>
                SystemAreaRequested?.Invoke(this, EventArgs.Empty);
            flyout.Items.Add(systemAreaItem);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var exitItem = new MenuFlyoutItem
        {
            Text = "Exit SeanShell",
            Icon = CreateMenuIcon("\uE8BB"),
        };
        exitItem.Click += (_, _) =>
            ExitRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(exitItem);

        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(anchor);
        args.Handled = true;
    }

    private static void AddSystemTool(
        MenuFlyout flyout,
        string title,
        string glyph,
        string target)
    {
        var item = new MenuFlyoutItem
        {
            Text = title,
            Icon = CreateMenuIcon(glyph),
        };
        item.Click += (_, _) => LaunchShellTarget(target);
        flyout.Items.Add(item);
    }

    private void AddPowerMenu(MenuFlyout flyout)
    {
        var powerMenu = new MenuFlyoutSubItem
        {
            Text = "Power",
            Icon = CreateMenuIcon("\uE7E8"),
        };

        var lockItem = new MenuFlyoutItem
        {
            Text = "Lock",
            Icon = CreateMenuIcon("\uE72E"),
        };
        lockItem.Click += (_, _) => LaunchSessionAction(SessionAction.Lock);
        powerMenu.Items.Add(lockItem);
        powerMenu.Items.Add(new MenuFlyoutSeparator());

        AddConfirmedSessionAction(
            powerMenu,
            SessionAction.SignOut,
            "Sign out",
            "\uE8AC");
        AddConfirmedSessionAction(
            powerMenu,
            SessionAction.Restart,
            "Restart",
            "\uE777");
        AddConfirmedSessionAction(
            powerMenu,
            SessionAction.ShutDown,
            "Shut down",
            "\uE7E8");
        flyout.Items.Add(powerMenu);
    }

    private void AddConfirmedSessionAction(
        MenuFlyoutSubItem powerMenu,
        SessionAction action,
        string title,
        string glyph)
    {
        var item = new MenuFlyoutItem
        {
            Text = title,
            Icon = CreateMenuIcon(glyph),
        };
        item.Click += async (_, _) =>
            await ConfirmSessionActionAsync(action, title).ConfigureAwait(true);
        powerMenu.Items.Add(item);
    }

    private async Task ConfirmSessionActionAsync(
        SessionAction action,
        string title)
    {
        _autoHideTimer.Stop();
        _modalDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = DockRoot.XamlRoot,
                Title = $"{title} Windows?",
                Content = action switch
                {
                    SessionAction.SignOut =>
                        "Open applications may prevent sign out. Save your work before continuing.",
                    SessionAction.Restart =>
                        "Windows will restart immediately. Save your work before continuing.",
                    SessionAction.ShutDown =>
                        "Windows will shut down immediately. Save your work before continuing.",
                    _ => string.Empty,
                },
                PrimaryButtonText = title,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                LaunchSessionAction(action);
            }
        }
        finally
        {
            _modalDialogOpen = false;
            ScheduleAutoHide();
        }
    }

    private static void LaunchSessionAction(SessionAction action)
    {
        var (fileName, arguments) = action switch
        {
            SessionAction.Lock =>
                ("rundll32.exe", "user32.dll,LockWorkStation"),
            SessionAction.SignOut => ("shutdown.exe", "/l"),
            SessionAction.Restart => ("shutdown.exe", "/r /t 0"),
            SessionAction.ShutDown => ("shutdown.exe", "/s /t 0"),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        Process.Start(
            new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
    }

    private void OnSystemAreaClicked(object sender, RoutedEventArgs e)
    {
        SystemAreaRequested?.Invoke(this, EventArgs.Empty);
        ScheduleAutoHide();
    }

    private void OnOpenDateTimeSettingsClicked(object sender, RoutedEventArgs e)
    {
        LaunchShellTarget("ms-settings:dateandtime");
        ScheduleAutoHide();
    }

    private void OnClockFlyoutOpening(object sender, object e)
    {
        DockCalendarView.SetDisplayDate(_clockTimestamp);
    }

    private void OnClockFlyoutClosed(object sender, object e)
    {
        ScheduleAutoHide();
    }

    private void OnOpenSystemSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string settingsUri })
        {
            LaunchShellTarget(settingsUri);
        }

        ScheduleAutoHide();
    }

    private void OnQuickSettingsOpening(object sender, object e)
    {
        _quickAudioControlsActive = false;
        _lastSystemStatus = _systemStatus.Capture();
        var systemText =
            SystemStatusTextFormatter.Format(_lastSystemStatus);
        var audioText = ApplyQuickAudioSnapshot(_audioEndpoint.Capture());
        QuickNetworkText.Text = systemText.Network;
        QuickPowerText.Text = systemText.Power;
        UpdateDockSystemIndicators(systemText, audioText);
        _quickAudioControlsActive = true;
    }

    private void OnQuickSettingsClosed(object sender, object e)
    {
        _quickAudioControlsActive = false;
        ScheduleAutoHide();
    }

    private void OnQuickVolumeChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_quickAudioControlsActive || _updatingQuickAudioControls)
        {
            return;
        }

        ApplyQuickAudioSnapshot(
            _audioEndpoint.SetVolume((int)Math.Round(e.NewValue)));
    }

    private void OnQuickSoundMuteToggled(
        object sender,
        RoutedEventArgs e)
    {
        if (!_quickAudioControlsActive || _updatingQuickAudioControls)
        {
            return;
        }

        ApplyQuickAudioSnapshot(
            _audioEndpoint.SetMuted(QuickSoundMuteToggle.IsOn));
    }

    private AudioEndpointDisplayText ApplyQuickAudioSnapshot(
        AudioEndpointSnapshot snapshot)
    {
        _lastAudioStatus = snapshot;
        var text = AudioEndpointTextFormatter.Format(snapshot);
        _updatingQuickAudioControls = true;
        try
        {
            QuickSoundText.Text = text.Summary;
            QuickVolumeSlider.IsEnabled = snapshot.IsAvailable;
            QuickSoundMuteToggle.IsEnabled = snapshot.IsAvailable;
            QuickVolumeSlider.Value = snapshot.VolumePercent ?? 0;
            QuickSoundMuteToggle.IsOn = snapshot.IsMuted;
            AutomationProperties.SetName(
                QuickSoundText,
                text.AccessibleSummary);
        }
        finally
        {
            _updatingQuickAudioControls = false;
        }

        UpdateDockSystemIndicators(
            SystemStatusTextFormatter.Format(_lastSystemStatus),
            text);
        return text;
    }

    private void RefreshDockSystemIndicators()
    {
        _lastSystemStatus = _systemStatus.Capture();
        _lastAudioStatus = _audioEndpoint.Capture();
        UpdateDockSystemIndicators(
            SystemStatusTextFormatter.Format(_lastSystemStatus),
            AudioEndpointTextFormatter.Format(_lastAudioStatus));
    }

    private void UpdateDockSystemIndicators(
        SystemStatusDisplayText systemText,
        AudioEndpointDisplayText audioText)
    {
        var networkUnavailable = _lastSystemStatus.NetworkAvailable != true;
        DockNetworkOfflineMark.Visibility =
            networkUnavailable ? Visibility.Visible : Visibility.Collapsed;
        DockNetworkStatusIcon.Opacity =
            _lastSystemStatus.NetworkAvailable is null ? 0.45 : 1;
        var networkBrush = GetThemeBrush(
            networkUnavailable
                ? "SystemFillColorCriticalBrush"
                : "TextFillColorPrimaryBrush");
        DockNetworkStatusIcon.Foreground = networkBrush;
        DockNetworkOfflineMark.Foreground = networkBrush;

        DockAudioStatusIcon.Glyph =
            _lastAudioStatus.IsMuted ? "\uE74F" : "\uE767";
        DockAudioStatusIcon.Opacity =
            _lastAudioStatus.IsAvailable ? 1 : 0.45;
        DockAudioStatusIcon.Foreground = GetThemeBrush(
            _lastAudioStatus.IsAvailable && !_lastAudioStatus.IsMuted
                ? "TextFillColorPrimaryBrush"
                : "TextFillColorSecondaryBrush");

        var batteryIndicator = BatteryIndicatorResolver.Resolve(_lastSystemStatus);
        DockPowerStatusIcon.Glyph = batteryIndicator.Kind == BatteryIndicatorKind.Charging
            ? ChargingBatteryGlyphs[batteryIndicator.Level]
            : BatteryGlyphs[batteryIndicator.Level];
        DockPowerStatusIcon.Opacity =
            batteryIndicator.Emphasis == BatteryIndicatorEmphasis.Unavailable
                ? 0.45
                : 1;
        var powerBrush = GetThemeBrush(batteryIndicator.Emphasis switch
        {
            BatteryIndicatorEmphasis.Critical => "SystemFillColorCriticalBrush",
            BatteryIndicatorEmphasis.Caution => "SystemFillColorCautionBrush",
            BatteryIndicatorEmphasis.Charging => "AccentTextFillColorPrimaryBrush",
            BatteryIndicatorEmphasis.Unavailable => "TextFillColorDisabledBrush",
            _ => "TextFillColorPrimaryBrush",
        });
        DockPowerStatusIcon.Foreground = powerBrush;
        DockPowerPercentText.Foreground = powerBrush;
        DockPowerPercentText.Text =
            _lastSystemStatus.BatteryPercent?.ToString(
                CultureInfo.CurrentCulture) ?? string.Empty;
        DockPowerPercentText.Visibility =
            _lastSystemStatus.HasBattery &&
            _lastSystemStatus.BatteryPercent is not null
                ? Visibility.Visible
                : Visibility.Collapsed;

        AutomationProperties.SetName(
            QuickSettingsButton,
            $"{systemText.AccessibleSummary} {audioText.AccessibleSummary}");
        ToolTipService.SetToolTip(
            QuickSettingsButton,
            $"{systemText.Network}\n{audioText.Summary}\n{systemText.Power}");
    }

    private static Brush? GetThemeBrush(string resourceKey) =>
        Application.Current.Resources.TryGetValue(resourceKey, out var value) &&
        value is Brush brush
            ? brush
            : null;

    private static void LaunchShellTarget(string target) =>
        Process.Start(
            new ProcessStartInfo(target)
            {
                UseShellExecute = true,
            });

    private void OnShowDesktopClicked(object sender, RoutedEventArgs e)
    {
        ShowDesktopRequested?.Invoke(this, EventArgs.Empty);
        ScheduleAutoHide();
    }

    private enum SessionAction
    {
        Lock,
        SignOut,
        Restart,
        ShutDown,
    }

    private async void OnPinnedApplicationClicked(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PinnedDockItemViewModel item)
        {
            return;
        }

        PlayLaunchFeedback(item);

        try
        {
            var clickAction = TaskbarClickActionResolver.Resolve(
                KeyboardModifierStateReader.IsShiftPressed(),
                KeyboardModifierStateReader.IsControlPressed());
            if (clickAction == TaskbarClickAction.OpenNewInstance)
            {
                await OpenNewInstanceAsync(item.Command).ConfigureAwait(true);
            }
            else if (clickAction == TaskbarClickAction.OpenElevatedInstance)
            {
                await OpenApplicationInstanceAsync(
                        item.Command,
                        elevated: true)
                    .ConfigureAwait(true);
            }
            else
            {
                await item.Command.ExecuteAsync(CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Launch failed";
            ToolTipService.SetToolTip(
                DockCountText,
                $"Unable to open {item.Title}: {exception.Message}");
        }
        finally
        {
            ScheduleAutoHide();
        }
    }

    private void PlayLaunchFeedback(object dataContext)
    {
        var target = _dockMotionItems.FirstOrDefault(
            element => ReferenceEquals(element.DataContext, dataContext));
        if (target is not null)
        {
            PlayLaunchFeedback(target);
        }
    }

    private void PlayLaunchFeedback(FrameworkElement target)
    {
        var motion = DockLaunchMotion.Resolve(_reducedEffects);
        if (motion.DurationMilliseconds == 0 || !target.IsLoaded)
        {
            return;
        }

        DismissDockMagnifier();
        if (_launchFeedbackTransitions.Remove(target, out var previous))
        {
            previous.Stop();
        }

        var translate = target.RenderTransform as TranslateTransform;
        if (translate is null)
        {
            translate = new TranslateTransform();
            target.RenderTransform = translate;
        }

        translate.Y = 0;
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(
                motion.DurationMilliseconds)),
        };
        foreach (var frame in motion.Frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(
                    motion.DurationMilliseconds * frame.Progress)),
                Value = frame.TranslationY,
                EasingFunction = new CubicEase
                {
                    EasingMode = frame.TranslationY == 0
                        ? EasingMode.EaseIn
                        : EasingMode.EaseOut,
                },
            });
        }

        Storyboard.SetTarget(animation, translate);
        Storyboard.SetTargetProperty(animation, nameof(TranslateTransform.Y));
        var transition = new Storyboard();
        transition.Children.Add(animation);
        transition.Completed += (_, _) =>
        {
            if (!_launchFeedbackTransitions.Remove(target, out var current) ||
                !ReferenceEquals(current, transition))
            {
                return;
            }

            translate.Y = 0;
            transition.Stop();
        };
        _launchFeedbackTransitions[target] = transition;
        transition.Begin();
    }

    private async void OnPinnedApplicationsDragCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != DataPackageOperation.Move ||
            PinOrderRequested is not { } handler)
        {
            return;
        }

        try
        {
            var visibleOrder = PinnedItems
                .Select(static item => item.Id)
                .ToArray();
            if (!await handler(visibleOrder).ConfigureAwait(true))
            {
                RefreshPinnedItems();
            }
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Reorder failed";
            ToolTipService.SetToolTip(DockCountText, exception.Message);
            RefreshPinnedItems();
        }
    }

    private void OnRunningApplicationsDragCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != DataPackageOperation.Move)
        {
            return;
        }

        _windowGroupOrder = Items
            .Select(static item => item.GroupKey)
            .ToArray();
        ResetWindowOverflowControls();
    }

    private void OnPinnedApplicationContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not PinnedDockItemViewModel item)
        {
            return;
        }

        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var flyout = CreateDockMenuFlyout();
        AddPinnedApplicationActions(
            flyout.Items,
            item.Command,
            includeLeadingSeparator: false);
        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(element);
        args.Handled = true;
    }

    private void AddPinAction(
        IList<MenuFlyoutItemBase> items,
        DockItemViewModel dockItem)
    {
        var pinnedApplication = TaskbarDockPinResolver.FindPinnedApplication(
            _pinnedApplications,
            dockItem.Windows);
        if (pinnedApplication is not null)
        {
            AddPinnedApplicationActions(
                items,
                pinnedApplication,
                includeLeadingSeparator: true);
            return;
        }

        var candidates = TaskbarDockPinResolver.FindPinCandidates(
            _availableApplications,
            dockItem.Windows);
        if (candidates.Count == 0)
        {
            return;
        }

        items.Add(new MenuFlyoutSeparator());
        if (candidates.Count == 1)
        {
            items.Add(CreatePinMenuItem(candidates[0], shouldPin: true));
            return;
        }

        var pinMenu = new MenuFlyoutSubItem
        {
            Text = "Pin to Dock",
            Icon = CreatePinIcon(shouldPin: true),
        };
        foreach (var candidate in candidates)
        {
            var candidateItem = new MenuFlyoutItem
            {
                Text = candidate.Title,
            };
            candidateItem.Click += async (_, _) =>
                await RequestPinChangeAsync(candidate, shouldPin: true)
                    .ConfigureAwait(true);
            pinMenu.Items.Add(candidateItem);
        }

        items.Add(pinMenu);
    }

    private void AddOpenNewInstanceAction(
        IList<MenuFlyoutItemBase> items,
        DockItemViewModel dockItem)
    {
        var candidates = TaskbarDockPinResolver.FindApplicationCandidates(
            _availableApplications,
            dockItem.Windows);
        if (candidates.Count == 0)
        {
            return;
        }

        items.Add(new MenuFlyoutSeparator());
        if (candidates.Count == 1)
        {
            items.Add(CreateOpenNewInstanceMenuItem(candidates[0]));
            return;
        }

        var openMenu = new MenuFlyoutSubItem
        {
            Text = "Open new instance",
            Icon = CreateOpenNewInstanceIcon(),
        };
        foreach (var candidate in candidates)
        {
            var candidateItem = new MenuFlyoutItem
            {
                Text = candidate.Title,
            };
            candidateItem.Click += async (_, _) =>
                await OpenNewInstanceAsync(candidate).ConfigureAwait(true);
            openMenu.Items.Add(candidateItem);
        }

        items.Add(openMenu);
    }

    private void AddRunningOrderActions(
        IList<MenuFlyoutItemBase> items,
        DockItemViewModel dockItem)
    {
        items.Add(new MenuFlyoutSeparator());
        items.Add(CreateRunningMoveMenuItem(
            dockItem,
            TaskbarWindowMoveDirection.Left));
        items.Add(CreateRunningMoveMenuItem(
            dockItem,
            TaskbarWindowMoveDirection.Right));
    }

    private MenuFlyoutItem CreateRunningMoveMenuItem(
        DockItemViewModel dockItem,
        TaskbarWindowMoveDirection direction)
    {
        var isLeft = direction == TaskbarWindowMoveDirection.Left;
        var item = new MenuFlyoutItem
        {
            Text = isLeft ? "Move left" : "Move right",
            Icon = CreateMenuIcon(isLeft ? "\uE76B" : "\uE76C"),
            IsEnabled = TaskbarWindowOrder.CanMove(
                _windowGroupOrder,
                dockItem.GroupKey,
                direction),
        };
        item.Click += (_, _) =>
        {
            _windowGroupOrder = TaskbarWindowOrder.Move(
                _windowGroupOrder,
                dockItem.GroupKey,
                direction);
            RefreshWindowItems();
        };
        return item;
    }

    private void AddRunningExecutableActions(
        IList<MenuFlyoutItemBase> items,
        DockItemViewModel dockItem)
    {
        if (TaskbarDockPinResolver.FindPinnedApplication(
                _pinnedApplications,
                dockItem.Windows) is not null)
        {
            return;
        }

        var candidates = TaskbarDockPinResolver.FindApplicationCandidates(
                _availableApplications,
                dockItem.Windows)
            .Where(candidate =>
                IsVerifiedLocalExecutable(
                    candidate.ApplicationExecutablePath))
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        items.Add(new MenuFlyoutSeparator());
        if (candidates.Length == 1)
        {
            AddExecutableActions(
                items,
                candidates[0],
                includeTrailingSeparator: false);
            return;
        }

        var toolsMenu = new MenuFlyoutSubItem
        {
            Text = "Application tools",
            Icon = CreateMenuIcon("\uE90F"),
        };
        foreach (var candidate in candidates)
        {
            var candidateMenu = new MenuFlyoutSubItem
            {
                Text = candidate.Title,
            };
            AddExecutableActions(
                candidateMenu.Items,
                candidate,
                includeTrailingSeparator: false);
            toolsMenu.Items.Add(candidateMenu);
        }

        items.Add(toolsMenu);
    }

    private void ShowOpenNewInstancePicker(
        IReadOnlyList<ShellCommand> candidates,
        FrameworkElement anchor,
        bool elevated)
    {
        _autoHideTimer.Stop();
        _contextMenuOpen = true;

        var flyout = CreateDockMenuFlyout();
        foreach (var candidate in candidates)
        {
            var item = new MenuFlyoutItem
            {
                Text = candidate.Title,
                Icon = CreateOpenNewInstanceIcon(),
            };
            item.Click += async (_, _) =>
                await OpenApplicationInstanceAsync(candidate, elevated)
                    .ConfigureAwait(true);
            flyout.Items.Add(item);
        }

        flyout.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(anchor);
    }

    private MenuFlyoutItem CreateOpenNewInstanceMenuItem(
        ShellCommand application)
    {
        var item = new MenuFlyoutItem
        {
            Text = "Open new instance",
            Icon = CreateOpenNewInstanceIcon(),
        };
        item.Click += async (_, _) =>
            await OpenNewInstanceAsync(application).ConfigureAwait(true);
        return item;
    }

    private static FontIcon CreateOpenNewInstanceIcon() =>
        new()
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                "Segoe Fluent Icons"),
            Glyph = "\uE8A7",
        };

    private async Task OpenNewInstanceAsync(ShellCommand application)
    {
        try
        {
            await application.ExecuteAsync(CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Launch failed";
            ToolTipService.SetToolTip(
                DockCountText,
                $"Unable to open {application.Title}: {exception.Message}");
        }
    }

    private async Task OpenApplicationInstanceAsync(
        ShellCommand application,
        bool elevated)
    {
        if (!elevated)
        {
            await OpenNewInstanceAsync(application).ConfigureAwait(true);
            return;
        }

        var executablePath = application.ApplicationExecutablePath;
        if (!IsVerifiedLocalExecutable(executablePath))
        {
            DockCountText.Text = "Elevated launch unavailable";
            ToolTipService.SetToolTip(
                DockCountText,
                $"{application.Title} does not expose a verified local executable.");
            return;
        }

        RunApplicationElevated(application, executablePath!);
    }

    private void AddPinnedApplicationActions(
        IList<MenuFlyoutItemBase> items,
        ShellCommand application,
        bool includeLeadingSeparator)
    {
        if (includeLeadingSeparator)
        {
            items.Add(new MenuFlyoutSeparator());
        }

        AddExecutableActions(items, application);
        items.Add(CreatePinMenuItem(application, shouldPin: false));
        items.Add(new MenuFlyoutSeparator());
        items.Add(CreateMoveMenuItem(
            application,
            PinnedApplicationMoveDirection.Left));
        items.Add(CreateMoveMenuItem(
            application,
            PinnedApplicationMoveDirection.Right));
    }

    private void AddExecutableActions(
        IList<MenuFlyoutItemBase> items,
        ShellCommand application,
        bool includeTrailingSeparator = true)
    {
        var executablePath = application.ApplicationExecutablePath;
        if (!IsVerifiedLocalExecutable(executablePath))
        {
            return;
        }

        var openLocationItem = new MenuFlyoutItem
        {
            Text = "Open file location",
            Icon = CreateMenuIcon("\uEC50"),
        };
        openLocationItem.Click += (_, _) =>
            OpenExecutableLocation(executablePath!);
        items.Add(openLocationItem);

        var runElevatedItem = new MenuFlyoutItem
        {
            Text = "Run as administrator",
            Icon = CreateMenuIcon("\uE7EF"),
        };
        runElevatedItem.Click += (_, _) =>
            RunApplicationElevated(application, executablePath!);
        items.Add(runElevatedItem);
        if (includeTrailingSeparator)
        {
            items.Add(new MenuFlyoutSeparator());
        }
    }

    private static bool IsVerifiedLocalExecutable(string? path) =>
        ApplicationExecutablePolicy.IsSupportedLocalPath(path) &&
        File.Exists(path);

    private void OpenExecutableLocation(string executablePath)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"/select,\"{executablePath}\"",
                    UseShellExecute = true,
                });
        }
        catch (Exception exception)
        {
            ReportApplicationActionFailure("Open location failed", exception);
        }
    }

    private void RunApplicationElevated(
        ShellCommand application,
        string executablePath)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo(executablePath)
                {
                    Arguments = application.ApplicationArguments ?? string.Empty,
                    UseShellExecute = true,
                    Verb = "runas",
                });
        }
        catch (Exception exception)
        {
            ReportApplicationActionFailure("Elevated launch failed", exception);
        }
    }

    private void ReportApplicationActionFailure(
        string title,
        Exception exception)
    {
        DockCountText.Text = title;
        ToolTipService.SetToolTip(
            DockCountText,
            exception.Message);
    }

    private MenuFlyoutItem CreatePinMenuItem(
        ShellCommand command,
        bool shouldPin)
    {
        var item = new MenuFlyoutItem
        {
            Text = shouldPin ? "Pin to Dock" : "Unpin from Dock",
            Icon = CreatePinIcon(shouldPin),
        };
        item.Click += async (_, _) =>
            await RequestPinChangeAsync(command, shouldPin).ConfigureAwait(true);
        return item;
    }

    private static FontIcon CreatePinIcon(bool shouldPin) =>
        new()
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                "Segoe Fluent Icons"),
            Glyph = shouldPin ? "\uE718" : "\uE77A",
        };

    private MenuFlyoutItem CreateMoveMenuItem(
        ShellCommand application,
        PinnedApplicationMoveDirection direction)
    {
        var applicationIds = _pinnedApplications
            .Select(static command => command.Id)
            .ToArray();
        var isLeft = direction == PinnedApplicationMoveDirection.Left;
        var item = new MenuFlyoutItem
        {
            Text = isLeft ? "Move left" : "Move right",
            Icon = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                    "Segoe Fluent Icons"),
                Glyph = isLeft ? "\uE76B" : "\uE76C",
            },
            IsEnabled = PinnedApplicationOrder.CanMove(
                applicationIds,
                application.Id,
                direction),
        };
        item.Click += async (_, _) =>
            await RequestPinMoveAsync(application, direction).ConfigureAwait(true);
        return item;
    }

    private async Task RequestPinChangeAsync(
        ShellCommand command,
        bool shouldPin)
    {
        var handler = PinChangedRequested;
        if (handler is null)
        {
            return;
        }

        try
        {
            if (!await handler(command, shouldPin).ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    "The pinned application preference was not changed.");
            }
        }
        catch (Exception exception)
        {
            DockCountText.Text = shouldPin ? "Pin failed" : "Unpin failed";
            ToolTipService.SetToolTip(DockCountText, exception.Message);
        }
    }

    private async Task RequestPinMoveAsync(
        ShellCommand application,
        PinnedApplicationMoveDirection direction)
    {
        var handler = PinMoveRequested;
        if (handler is null)
        {
            return;
        }

        try
        {
            if (!await handler(application, direction).ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    "The pinned application order was not changed.");
            }
        }
        catch (Exception exception)
        {
            DockCountText.Text = "Move failed";
            ToolTipService.SetToolTip(DockCountText, exception.Message);
        }
    }

    private void OnShellStateChanged(object? sender, ShellState state)
    {
        if (state.Mode == ShellMode.Gaming)
        {
            _autoHideTimer.Stop();
            DismissWindowPreview();
            DismissDockMagnifier();
            AppWindow.Hide();
            return;
        }

        ShowDock();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        DismissWindowPreview();
        DismissDockMagnifier();
        AppWindow.Hide();
    }
}
