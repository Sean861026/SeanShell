using Microsoft.UI.Xaml;
using SeanShell.Windows;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeanShell.App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const uint SpaceVirtualKey = 0x20;
    private readonly LauncherWindow _launcherWindow;
    private readonly DockWindow _dockWindow;
    private GlobalHotKey? _launcherHotKey;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        var app = (App)Application.Current;
        _launcherWindow = new LauncherWindow(app.LauncherSearch);
        _dockWindow = new DockWindow(app.DesktopWindows, app.ShellState);

        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.LauncherRequested += OnLauncherRequested;
        }

        RegisterLauncherHotKey();
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        _dockWindow.ShowDock();
    }

    private void RegisterLauncherHotKey()
    {
        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _launcherHotKey = new GlobalHotKey(
                windowHandle,
                HotKeyModifiers.Alt | HotKeyModifiers.NoRepeat,
                SpaceVirtualKey);
            _launcherHotKey.Pressed += OnLauncherRequested;
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetShortcutUnavailable(exception.Message);
            }
        }
    }

    private void OnLauncherRequested(object? sender, EventArgs e)
    {
        _ = _launcherWindow.ShowLauncherAsync();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _launcherHotKey?.Dispose();
        _dockWindow.Shutdown();
        _launcherWindow.Shutdown();
    }
}
