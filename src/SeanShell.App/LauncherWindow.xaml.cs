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

    public async Task ShowLauncherAsync()
    {
        var firstUsableStopwatch = Stopwatch.StartNew();
        CenterOnCurrentDisplay();
        AppWindow.Show();
        Activate();

        SearchBox.Text = string.Empty;
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
        await RefreshResultsAsync(string.Empty).ConfigureAwait(true);
        firstUsableStopwatch.Stop();
        _performanceMonitor.RecordFirstUsable(firstUsableStopwatch.Elapsed);
    }

    public void HideLauncher()
    {
        _searchCancellation?.Cancel();
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
        AppWindow.SetPresenter(presenter);
        ResizeAndCenterOnCurrentDisplay();
    }

    private void CenterOnCurrentDisplay()
    {
        ResizeAndCenterOnCurrentDisplay();
    }

    private void ResizeAndCenterOnCurrentDisplay()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scaleFactor = DisplayDpiService.GetWindowScaleFactor(windowHandle);
        var placement = LauncherWindowPlacement.Calculate(
            workArea.X,
            workArea.Y,
            workArea.Width,
            workArea.Height,
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
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        try
        {
            await Task.Delay(60, _searchCancellation.Token).ConfigureAwait(true);
            await RefreshResultsAsync(SearchBox.Text, _searchCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
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
        }
        finally
        {
            SearchProgress.IsActive = false;
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
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
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
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
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
