using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SeanShell.Core;
using SeanShell.Windows;
using Windows.Graphics;
using Windows.System;

namespace SeanShell.App;

public sealed partial class LauncherWindow : Window
{
    private const int WindowWidth = 760;
    private const int WindowHeight = 620;
    private readonly InstalledApplicationProvider _installedApplications;
    private readonly LauncherPerformanceMonitor _performanceMonitor;
    private readonly LauncherSearchService _searchService;
    private readonly LauncherOperationLifetime _lifetime = new();
    private readonly HashSet<string> _pinnedApplicationIds =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _searchCancellation;
    private bool _allowClose;
    private bool _suppressSearchRefresh;

    public LauncherWindow(
        LauncherSearchService searchService,
        InstalledApplicationProvider installedApplications,
        LauncherPerformanceMonitor performanceMonitor)
    {
        _searchService = searchService;
        _installedApplications = installedApplications;
        _performanceMonitor = performanceMonitor;
        InitializeComponent();

        ApplyDisplayDensity(((App)Application.Current).SettingsLoad.Settings.DisplayDensity);
        ResultsList.ItemsSource = Results;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(LauncherTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigurePresenter();

        AppWindow.Closing += OnWindowClosing;
    }

    public ObservableCollection<LauncherResultViewModel> Results { get; } = [];

    public event Func<ShellCommand, bool, Task<bool>>? PinChangedRequested;

    public void SetPinnedApplicationIds(IEnumerable<string> applicationIds)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        if (_lifetime.IsShutdown)
        {
            return;
        }

        _pinnedApplicationIds.Clear();
        _pinnedApplicationIds.UnionWith(applicationIds);
        foreach (var result in Results)
        {
            result.SetPinned(_pinnedApplicationIds.Contains(result.Command.Id));
        }
    }

    private void ApplyDisplayDensity(ShellDisplayDensity density)
    {
        if (density != ShellDisplayDensity.Compact)
        {
            return;
        }

        LauncherContent.Padding = new Thickness(16, 8, 16, 12);
        LauncherContent.RowSpacing = 8;
        ResultsList.ItemContainerStyle =
            (Style)Application.Current.Resources["SeanCompactLauncherResultItemStyle"];
    }

    public async Task ShowLauncherAsync(DisplayMonitorSnapshot? targetMonitor = null)
    {
        if (!_lifetime.TryShow(out var session))
        {
            return;
        }

        var firstUsableStopwatch = Stopwatch.StartNew();
        var searchToken = BeginSearch();
        UpdateInteractionState();
        ResizeAndCenterOnDisplay(targetMonitor);
        AppWindow.Show();
        Activate();

        _suppressSearchRefresh = true;
        try
        {
            SearchBox.Text = string.Empty;
        }
        finally
        {
            _suppressSearchRefresh = false;
        }

        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
        try
        {
            await RefreshResultsAsync(string.Empty, session, searchToken).ConfigureAwait(true);
            if (IsCurrentSearch(session, searchToken))
            {
                firstUsableStopwatch.Stop();
                _performanceMonitor.RecordFirstUsable(firstUsableStopwatch.Elapsed);
            }
        }
        catch (OperationCanceledException) when (searchToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Search unavailable", exception, DiagnosticEventKind.LauncherSearchFailed,
                IsCurrentSearch(session, searchToken));
        }
    }

    public void HideLauncher()
    {
        if (_lifetime.IsShutdown)
        {
            return;
        }

        _lifetime.Hide();
        _searchCancellation?.Cancel();
        SearchProgress.IsActive = false;
        AppWindow.Hide();
    }

    public void SetReducedEffects(bool enabled)
    {
        if (_lifetime.IsShutdown)
        {
            return;
        }

        SystemBackdrop = enabled
            ? null
            : new MicaBackdrop { Kind = MicaKind.BaseAlt };
        LauncherRoot.Background = enabled
            ? Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush
            : null;
    }

    public void Shutdown()
    {
        if (_lifetime.IsShutdown)
        {
            return;
        }

        // Invalidate continuations before cancellation or native window teardown.
        _lifetime.Shutdown();
        _allowClose = true;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        Close();
    }

    private CancellationToken BeginSearch()
    {
        // Each async search keeps its own token. Disposing the previous source
        // here can race provider and icon tasks still observing that token.
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        return _searchCancellation.Token;
    }

    private void ConfigurePresenter()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        ResizeAndCenterOnCurrentDisplay();
    }

    private void ResizeAndCenterOnCurrentDisplay()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scaleFactor = DisplayDpiService.GetWindowScaleFactor(windowHandle);
        MoveAndResize(workArea.X, workArea.Y, workArea.Width, workArea.Height, scaleFactor);
    }

    private void ResizeAndCenterOnDisplay(DisplayMonitorSnapshot? targetMonitor)
    {
        if (targetMonitor is null)
        {
            ResizeAndCenterOnCurrentDisplay();
            return;
        }

        MoveAndResize(
            targetMonitor.WorkAreaX,
            targetMonitor.WorkAreaY,
            targetMonitor.WorkAreaWidth,
            targetMonitor.WorkAreaHeight,
            DisplayDpiService.GetScaleFactor(targetMonitor.Handle));
    }

    private void MoveAndResize(
        int workAreaX,
        int workAreaY,
        int workAreaWidth,
        int workAreaHeight,
        double scaleFactor)
    {
        var placement = LauncherWindowPlacement.Calculate(
            workAreaX,
            workAreaY,
            workAreaWidth,
            workAreaHeight,
            WindowWidth,
            WindowHeight,
            scaleFactor);
        AppWindow.MoveAndResize(new RectInt32(
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height));
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchRefresh || !_lifetime.IsVisible)
        {
            return;
        }

        var session = _lifetime.Session;
        var searchToken = BeginSearch();
        try
        {
            var query = SearchBox.Text;
            await Task.Delay(60, searchToken).ConfigureAwait(true);
            await RefreshResultsAsync(query, session, searchToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Search unavailable", exception, DiagnosticEventKind.LauncherSearchFailed,
                IsCurrentSearch(session, searchToken));
        }
    }

    private bool IsCurrentSearch(long session, CancellationToken cancellationToken) =>
        _lifetime.IsCurrent(session) && !cancellationToken.IsCancellationRequested;

    private async Task RefreshResultsAsync(
        string query, long session, CancellationToken cancellationToken)
    {
        if (!IsCurrentSearch(session, cancellationToken))
        {
            return;
        }

        SearchProgress.IsActive = true;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            var searchStopwatch = Stopwatch.StartNew();
            var commands = await _searchService.SearchAsync(query, 8, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_lifetime.IsCurrent(session))
            {
                return;
            }

            searchStopwatch.Stop();
            _performanceMonitor.RecordSuccessfulSearch(searchStopwatch.Elapsed);

            Results.Clear();
            foreach (var command in commands)
            {
                var result = new LauncherResultViewModel(
                    command,
                    _pinnedApplicationIds.Contains(command.Id));
                Results.Add(result);
                _ = LoadResultIconAsync(result, cancellationToken);
            }

            ResultsList.SelectedIndex = Results.Count > 0 ? 0 : -1;
            EmptyState.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateInteractionState();
            ErrorInfoBar.IsOpen = false;
        }
        finally
        {
            if (IsCurrentSearch(session, cancellationToken))
            {
                SearchProgress.IsActive = false;
            }
        }
    }

    private async Task LoadResultIconAsync(
        LauncherResultViewModel result,
        CancellationToken cancellationToken)
    {
        try
        {
            var icon = await _installedApplications
                .GetIconAsync(result.Command, cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            await result.LoadIconAsync(icon, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine($"Unable to load a Launcher result icon. {exception}");
        }
    }

    private void ShowError(
        string title, Exception exception, DiagnosticEventKind kind, bool canDisplay)
    {
        Debug.WriteLine($"Launcher error: {exception}");
        ((App)Application.Current).Diagnostics.TryWrite(kind, exception);
        if (!canDisplay || !_lifetime.IsVisible)
        {
            return;
        }

        SearchProgress.IsActive = false;
        ErrorInfoBar.Title = title;
        ErrorInfoBar.Message = exception.Message;
        ErrorInfoBar.IsOpen = true;
    }

    private async void OnResultClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LauncherResultViewModel result)
        {
            await ExecuteAsync(result).ConfigureAwait(true);
        }
    }

    private async void OnPinClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: LauncherResultViewModel { CanPin: true } result,
            })
        {
            return;
        }

        var handler = PinChangedRequested;
        if (handler is null || !_lifetime.TryBegin(out var operation))
        {
            return;
        }

        var shouldPin = !result.IsPinned;
        try
        {
            UpdateInteractionState();
            ErrorInfoBar.IsOpen = false;
            if (await handler(result.Command, shouldPin).ConfigureAwait(true) &&
                _lifetime.CanApply(operation))
            {
                result.SetPinned(shouldPin);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Unable to change pin", exception, DiagnosticEventKind.LauncherActionFailed,
                _lifetime.CanApply(operation));
        }
        finally
        {
            _lifetime.Complete(operation);
            UpdateInteractionState();
        }
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_lifetime.IsVisible)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case VirtualKey.Enter when ResultsList.SelectedItem is LauncherResultViewModel result:
                e.Handled = true;
                await ExecuteAsync(result).ConfigureAwait(true);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                HideLauncher();
                break;
        }
    }

    private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            HideLauncher();
        }
    }

    private void MoveSelection(int delta)
    {
        if (!_lifetime.IsVisible || _lifetime.IsBusy || Results.Count == 0)
        {
            return;
        }

        var current = Math.Max(0, ResultsList.SelectedIndex);
        ResultsList.SelectedIndex = Math.Clamp(current + delta, 0, Results.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private async Task ExecuteAsync(LauncherResultViewModel result)
    {
        if (!_lifetime.TryBegin(out var operation))
        {
            return;
        }

        try
        {
            UpdateInteractionState();
            ErrorInfoBar.IsOpen = false;
            await result.Command.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);
            if (_lifetime.CanApply(operation))
            {
                HideLauncher();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Unable to open command", exception, DiagnosticEventKind.LauncherActionFailed,
                _lifetime.CanApply(operation));
        }
        finally
        {
            _lifetime.Complete(operation);
            UpdateInteractionState();
        }
    }

    private void UpdateInteractionState()
    {
        if (!_lifetime.IsVisible)
        {
            return;
        }

        ResultsList.IsEnabled = !_lifetime.IsBusy;
        ResultStatus.Text = _lifetime.IsBusy
            ? "Operation in progress..."
            : Results.Count == 1 ? "1 result" : $"{Results.Count} results";
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        HideLauncher();
    }
}
