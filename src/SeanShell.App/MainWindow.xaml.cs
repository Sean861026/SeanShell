using Microsoft.UI.Xaml;
using SeanShell.Core;
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
    private readonly IReadOnlyList<DockWindow> _dockWindows;
    private readonly ShellSettingsStore _settingsStore;
    private GlobalHotKey? _launcherHotKey;
    private LauncherShortcut? _registeredShortcut;
    private ShellSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        var app = (App)Application.Current;
        _settingsStore = app.SettingsStore;
        _settings = app.SettingsLoad.Settings;
        _launcherWindow = new LauncherWindow(app.LauncherSearch);
        _dockWindows = app.Displays.Capture()
            .Select(monitor => new DockWindow(app.DesktopWindows, app.ShellState, monitor))
            .ToArray();

        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.LauncherRequested += OnLauncherRequested;
            mainPage.DockAutoHideChanged += OnDockAutoHideChanged;
            mainPage.LauncherShortcutChanged += OnLauncherShortcutChanged;
        }

        RegisterLauncherHotKey(_settings.LauncherShortcut);
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.ShowDock();
            dockWindow.SetAutoHide(_settings.DockAutoHide);
        }
    }

    private void OnDockAutoHideChanged(bool enabled)
    {
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.SetAutoHide(enabled);
        }

        _settings = _settings with { DockAutoHide = enabled };
        PersistSettings();
    }

    private void OnLauncherShortcutChanged(LauncherShortcut shortcut)
    {
        if (_registeredShortcut == shortcut)
        {
            return;
        }

        if (!TryReplaceLauncherHotKey(shortcut, out var error))
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetShortcutUnavailable(shortcut, _registeredShortcut, error!);
            }

            return;
        }

        _settings = _settings with { LauncherShortcut = shortcut };
        var persisted = PersistSettings();
        if (RootFrame.Content is MainPage page)
        {
            page.SetShortcutApplied(shortcut, persisted);
        }
    }

    private void RegisterLauncherHotKey(LauncherShortcut shortcut)
    {
        if (!TryReplaceLauncherHotKey(shortcut, out var error) && RootFrame.Content is MainPage mainPage)
        {
            mainPage.SetShortcutUnavailable(shortcut, _registeredShortcut, error!);
        }
    }

    private bool TryReplaceLauncherHotKey(LauncherShortcut shortcut, out string? error)
    {
        var previousShortcut = _registeredShortcut;
        _launcherHotKey?.Dispose();
        _launcherHotKey = null;
        _registeredShortcut = null;

        try
        {
            _launcherHotKey = CreateLauncherHotKey(shortcut);
            _registeredShortcut = shortcut;
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        if (previousShortcut is not null)
        {
            try
            {
                _launcherHotKey = CreateLauncherHotKey(previousShortcut.Value);
                _registeredShortcut = previousShortcut;
            }
            catch (Exception restoreException)
            {
                error = $"{error} The previous shortcut could not be restored: {restoreException.Message}";
            }
        }

        return false;
    }

    private GlobalHotKey CreateLauncherHotKey(LauncherShortcut shortcut)
    {
        var modifiers = shortcut switch
        {
            LauncherShortcut.AltSpace => HotKeyModifiers.Alt,
            LauncherShortcut.ControlAltSpace => HotKeyModifiers.Control | HotKeyModifiers.Alt,
            LauncherShortcut.ControlShiftSpace => HotKeyModifiers.Control | HotKeyModifiers.Shift,
            _ => throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, null),
        };

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var hotKey = new GlobalHotKey(
            windowHandle,
            modifiers | HotKeyModifiers.NoRepeat,
            SpaceVirtualKey);
        hotKey.Pressed += OnLauncherRequested;
        return hotKey;
    }

    private bool PersistSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
            return true;
        }
        catch (Exception exception)
        {
            if (RootFrame.Content is MainPage mainPage)
            {
                mainPage.SetSettingsSaveFailed(exception.Message);
            }

            return false;
        }
    }

    private void OnLauncherRequested(object? sender, EventArgs e)
    {
        _ = _launcherWindow.ShowLauncherAsync();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _launcherHotKey?.Dispose();
        foreach (var dockWindow in _dockWindows)
        {
            dockWindow.Shutdown();
        }

        _launcherWindow.Shutdown();
    }
}
