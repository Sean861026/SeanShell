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
        var firstUsableStopwatch = Stopwatch.StartNew();
        var searchToken = BeginSearch();
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
            await RefreshResultsAsync(string.Empty, searchToken).ConfigureAwait(true);
            firstUsableStopwatch.Stop();
            _performanceMonitor.RecordFirstUsable(firstUsableStopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (searchToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Search unavailable", exception);
        }
    }

    public void HideLauncher()
    {
        _searchCancellation?.Cancel();
        SearchProgress.IsActive = false;
        AppWindow.Hide();
    }

    public void SetReducedEffects(bool enabled)
    {
        SystemBackdrop = enabled
            ? null
            : new MicaBackdrop { Kind = MicaKind.BaseAlt };
        LauncherRoot.Background = enabled
            ? Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush
            : null;
    }

    public void Shutdown()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        _allowClose = true;
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
        if (_suppressSearchRefresh)
        {
            return;
        }

        try
        {
            var searchToken = BeginSearch();
            var query = SearchBox.Text;
            await Task.Delay(60, searchToken).ConfigureAwait(true);
            await RefreshResultsAsync(query, searchToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Search unavailable", exception);
        }
    }

    private async Task RefreshResultsAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchProgress.IsActive = true;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            var searchStopwatch = Stopwatch.StartNew();
            var commands = await _searchService.SearchAsync(query, 8, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
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
            ResultStatus.Text = Results.Count == 1 ? "1 result" : $"{Results.Count} results";
            ErrorInfoBar.IsOpen = false;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
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
            await result.LoadIconAsync(icon).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine($"Unable to load a Launcher result icon. {exception}");
        }
    }

    private void ShowError(string title, Exception exception)
    {
        Debug.WriteLine($"Launcher error: {exception}");
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
        if (handler is null)
        {
            return;
        }

        var shouldPin = !result.IsPinned;
        try
        {
            ErrorInfoBar.IsOpen = false;
            if (await handler(result.Command, shouldPin).ConfigureAwait(true))
            {
                result.SetPinned(shouldPin);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Unable to change pin", exception);
        }
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
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
        if (Results.Count == 0)
        {
            return;
        }

        var current = Math.Max(0, ResultsList.SelectedIndex);
        ResultsList.SelectedIndex = Math.Clamp(current + delta, 0, Results.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private async Task ExecuteAsync(LauncherResultViewModel result)
    {
        try
        {
            ErrorInfoBar.IsOpen = false;
            await result.Command.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);
            HideLauncher();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("Unable to open command", exception);
        }
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
