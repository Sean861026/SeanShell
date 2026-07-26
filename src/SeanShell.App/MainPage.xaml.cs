using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeanShell.Core;
using SeanShell.Gaming;
using SeanShell.Plugins;
using SeanShell.Windows;

namespace SeanShell.App;

public sealed partial class MainPage : Page
{
    private readonly ShellStateStore _shellState;
    private readonly DesktopWindowService _desktopWindows;
    private readonly SystemMetricsProvider _systemMetrics;
    private int _displayCount;
    private readonly GamingModeManager _gamingMode;
    private readonly GamingDetectionPerformanceMonitor _gamingDetectionPerformance;
    private readonly GamingSessionRecorder _gamingSessions;
    private readonly LauncherPerformanceMonitor _launcherPerformance;
    private readonly PluginHost _pluginHost;
    private readonly ExternalPluginCatalog _externalPlugins;
    private readonly ExternalPluginTrustManager _externalPluginTrust;
    private readonly HashSet<string> _pendingPluginIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingExternalPluginIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _applyingSettings;
    private bool _applyingPluginDiagnostics;
    private bool _refreshing;
    private bool _refreshingExternalPlugins;
    private IReadOnlyList<ExternalPluginCandidate> _externalPluginCandidates = [];

    public MainPage()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _shellState = app.ShellState;
        _desktopWindows = app.DesktopWindows;
        _systemMetrics = app.SystemMetrics;
        _gamingMode = app.GamingMode;
        _gamingDetectionPerformance = app.GamingDetectionPerformance;
        _gamingSessions = app.GamingSessions;
        _launcherPerformance = app.LauncherPerformance;
        _pluginHost = app.PluginHost;
        _externalPlugins = app.ExternalPlugins;
        _externalPluginTrust = app.ExternalPluginTrust;
        _displayCount = app.Displays.Capture().Count;
        ApplyDisplayDensity(app.SettingsLoad.Settings.DisplayDensity);

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += OnRefreshTimerTick;
        ApplySettings(app.SettingsLoad.Settings);
        if (app.SettingsLoad.Warning is not null)
        {
            SetSettingsStatus(
                app.SettingsLoad.WasRecovered ? "Settings recovered" : "Safe settings active",
                app.SettingsLoad.Warning,
                InfoBarSeverity.Warning);
        }

        ApplyStartupStatus(app.StartupSession);
        if (_externalPluginTrust.Warning is not null)
        {
            SetSettingsStatus(
                "Plugin trust recovery active",
                _externalPluginTrust.Warning,
                InfoBarSeverity.Warning);
        }
    }

    public event EventHandler? LauncherRequested;

    public event Action<bool>? DockAutoHideChanged;

    public event Action<LauncherShortcut>? LauncherShortcutChanged;

    public event Action<ShellThemePreference>? ThemePreferenceChanged;

    public event Action<ShellDisplayDensity>? DisplayDensityChanged;

    public event Action<bool>? AutomaticGamingModeChanged;

    public event Action<string>? GameProcessRulesSaved;

    public event Action<bool>? ManualGamingModeChanged;

    public event Action<string, bool>? PluginEnabledChanged;

    public event Action<ExternalPluginCandidate, bool>? ExternalPluginConsentChanged;

    public event Action? ExternalPluginTrustClearRequested;

    public void SetExternalPluginConsentApplied(
        ExternalPluginCandidate candidate,
        bool approved)
    {
        if (candidate.Id is not null)
        {
            _pendingExternalPluginIds.Remove(candidate.Id);
        }

        ApplyExternalPluginCandidates();
        UpdateExternalPluginStatusSummary();
        SetSettingsStatus(
            approved ? "Plugin capabilities approved" : "Plugin consent revoked",
            approved
                ? $"{candidate.Name} is recorded as approved, but external execution remains blocked."
                : $"{candidate.Name} no longer has stored capability consent.",
            InfoBarSeverity.Success);
    }

    public void SetExternalPluginConsentFailed(
        ExternalPluginCandidate candidate,
        string message)
    {
        if (candidate.Id is not null)
        {
            _pendingExternalPluginIds.Remove(candidate.Id);
        }

        ApplyExternalPluginCandidates();
        UpdateExternalPluginStatusSummary();
        SetSettingsStatus("Plugin consent not changed", message, InfoBarSeverity.Warning);
    }

    public void SetExternalPluginTrustCleared()
    {
        _pendingExternalPluginIds.Clear();
        ApplyExternalPluginCandidates();
        UpdateExternalPluginStatusSummary();
        SetSettingsStatus(
            "External plugin consent cleared",
            "All stored publisher and capability approvals were revoked.",
            InfoBarSeverity.Success);
    }

    public void SetExternalPluginTrustClearFailed(string message)
    {
        ApplyExternalPluginCandidates();
        UpdateExternalPluginStatusSummary();
        SetSettingsStatus("Plugin consent not changed", message, InfoBarSeverity.Warning);
    }

    private void ApplyStartupStatus(StartupSessionResult? startup)
    {
        if (startup is null ||
            (startup.ConsecutiveFailures == 0 &&
             !startup.AutomaticStartupDisabled &&
             startup.Warning is null))
        {
            return;
        }

        var messages = new List<string>();
        if (startup.ConsecutiveFailures > 0)
        {
            messages.Add(
                $"Detected {startup.ConsecutiveFailures} incomplete startup " +
                $"attempt{(startup.ConsecutiveFailures == 1 ? string.Empty : "s")}.");
        }

        if (startup.AutomaticStartupDisabled)
        {
            messages.Add(
                "Automatic startup is disabled until this manual session becomes healthy.");
        }

        if (startup.Warning is not null)
        {
            messages.Add(startup.Warning);
        }

        StartupStatus.Title = startup.AutomaticStartupDisabled
            ? "Manual startup recovery"
            : "Startup recovery active";
        StartupStatus.Message = string.Join(" ", messages);
        StartupStatus.Severity = InfoBarSeverity.Warning;
        StartupStatus.IsOpen = true;
    }

    public void SetShortcutApplied(LauncherShortcut shortcut, bool persisted = true)
    {
        SelectShortcut(shortcut);
        ShortcutStatus.Text = $"Keyboard shortcut: {shortcut.GetDisplayName()}";
        SetSettingsStatus(
            persisted ? "Shortcut updated" : "Shortcut active for this session",
            persisted
                ? $"{shortcut.GetDisplayName()} now opens the Launcher."
                : $"{shortcut.GetDisplayName()} works now, but the settings file could not be updated.",
            persisted ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    public void SetShortcutUnavailable(LauncherShortcut requested, LauncherShortcut? restored, string reason)
    {
        if (restored is not null)
        {
            SelectShortcut(restored.Value);
            ShortcutStatus.Text = $"Keyboard shortcut: {restored.Value.GetDisplayName()}";
        }
        else
        {
            _applyingSettings = true;
            LauncherShortcutComboBox.SelectedItem = null;
            _applyingSettings = false;
            ShortcutStatus.Text = "No keyboard shortcut is active. Use the Open Launcher button.";
        }

        SetSettingsStatus(
            "Shortcut unavailable",
            restored is null
                ? $"Windows could not register {requested.GetDisplayName()}. Use Open Launcher or choose another shortcut. {reason}"
                : $"Windows could not register {requested.GetDisplayName()}. {restored.Value.GetDisplayName()} remains active. {reason}",
            InfoBarSeverity.Warning);
    }

    public void SetThemePreferenceApplied(ShellThemePreference theme, bool persisted)
    {
        SelectThemePreference(theme);
        SetSettingsStatus(
            persisted ? "Appearance saved" : "Appearance not saved",
            persisted
                ? "Restart SeanShell to apply the selected appearance to the dashboard, Launcher, and Dock."
                : "The appearance could not be saved, so the previous preference remains active.",
            persisted ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    public void SetDisplayDensityApplied(ShellDisplayDensity density, bool persisted)
    {
        SelectDisplayDensity(density);
        SetSettingsStatus(
            persisted ? "Display density saved" : "Display density not saved",
            persisted
                ? "Restart SeanShell to apply the selected spacing to the dashboard, Launcher, and Dock."
                : "The display density could not be saved, so the previous preference remains active.",
            persisted ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    public void SetSettingsSaveFailed(string message)
    {
        SetSettingsStatus("Settings not saved", message, InfoBarSeverity.Warning);
    }

    public void SetGamingSettingsApplied(string title, string message)
    {
        SetSettingsStatus(title, message, InfoBarSeverity.Success);
    }

    public void SetGameProcessRulesApplied(string rules, int count, bool persisted)
    {
        _applyingSettings = true;
        GameProcessRulesTextBox.Text = rules;
        _applyingSettings = false;
        if (persisted)
        {
            SetSettingsStatus(
                "Game rules updated",
                count == 0 ? "No automatic game rules are configured." : $"Saved {count} game process rule(s).",
                InfoBarSeverity.Success);
        }
    }

    public void SetGamingDetectionUnavailable(string message)
    {
        SetSettingsStatus(
            "Game detection paused",
            $"SeanShell could not read the current process snapshot and will retry automatically. {message}",
            InfoBarSeverity.Warning);
    }

    public void SetDisplayCount(int count)
    {
        _displayCount = count;
        UpdateDockStatus(_shellState.Current.Mode == ShellMode.Gaming);
        SetSettingsStatus(
            "Display layout updated",
            $"Dock windows now match {count} connected display{(count == 1 ? string.Empty : "s")}.",
            InfoBarSeverity.Success);
    }

    public void SetDisplayMonitoringUnavailable(string message)
    {
        SetSettingsStatus(
            "Display monitoring unavailable",
            $"Existing Dock windows remain active. Restart SeanShell after changing displays. {message}",
            InfoBarSeverity.Warning);
    }

    public void SetPluginEnabledApplied(string pluginId, string pluginName, bool enabled)
    {
        _pendingPluginIds.Remove(pluginId);
        ApplyPluginDiagnostics();
        SetSettingsStatus(
            enabled ? "Plugin enabled" : "Plugin disabled",
            enabled
                ? $"{pluginName} is available to Launcher and normal-mode providers."
                : $"{pluginName} is disabled and will remain off after restart.",
            InfoBarSeverity.Success);
    }

    public void SetPluginEnabledFailed(string pluginId, string message)
    {
        _pendingPluginIds.Remove(pluginId);
        ApplyPluginDiagnostics();
        SetSettingsStatus("Plugin setting not changed", message, InfoBarSeverity.Warning);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _shellState.StateChanged -= OnShellStateChanged;
        _shellState.StateChanged += OnShellStateChanged;
        _gamingMode.StatusChanged -= OnGamingModeStatusChanged;
        _gamingMode.StatusChanged += OnGamingModeStatusChanged;
        _gamingDetectionPerformance.Changed -= OnGamingDetectionPerformanceChanged;
        _gamingDetectionPerformance.Changed += OnGamingDetectionPerformanceChanged;
        _gamingSessions.Changed -= OnGamingSessionsChanged;
        _gamingSessions.Changed += OnGamingSessionsChanged;
        _pluginHost.DiagnosticsChanged -= OnPluginDiagnosticsChanged;
        _pluginHost.DiagnosticsChanged += OnPluginDiagnosticsChanged;
        _launcherPerformance.Changed -= OnLauncherPerformanceChanged;
        _launcherPerformance.Changed += OnLauncherPerformanceChanged;
        ApplyShellState(_shellState.Current);
        ApplyGamingModeStatus(_gamingMode.Current);
        ApplyGamingDetectionPerformance();
        ApplyGamingSessionHistory();
        ApplyPluginDiagnostics();
        _ = RefreshExternalPluginCandidatesAsync();
        ApplyLauncherPerformance();
        ApplyAdaptiveLayout(ActualWidth);
        if (_shellState.Current.Mode == ShellMode.Normal)
        {
            _refreshTimer.Start();
            _ = RefreshDashboardAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _shellState.StateChanged -= OnShellStateChanged;
        _gamingMode.StatusChanged -= OnGamingModeStatusChanged;
        _gamingDetectionPerformance.Changed -= OnGamingDetectionPerformanceChanged;
        _gamingSessions.Changed -= OnGamingSessionsChanged;
        _pluginHost.DiagnosticsChanged -= OnPluginDiagnosticsChanged;
        _launcherPerformance.Changed -= OnLauncherPerformanceChanged;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyAdaptiveLayout(e.NewSize.Width);
    }

    private void ApplyAdaptiveLayout(double width)
    {
        var medium = width >= 760;
        var wide = width >= 1280;
        var star = new GridLength(1, GridUnitType.Star);
        var hidden = new GridLength(0);

        HeaderActionColumn.Width = medium ? GridLength.Auto : hidden;
        Grid.SetRow(HeaderAction, medium ? 0 : 1);
        Grid.SetColumn(HeaderAction, medium ? 1 : 0);
        HeaderAction.HorizontalAlignment = medium
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

        MetricsColumn1.Width = medium ? star : hidden;
        MetricsColumn2.Width = wide ? star : hidden;
        MetricsColumn3.Width = wide ? star : hidden;
        Grid.SetRow(MemoryCard, medium ? 0 : 1);
        Grid.SetColumn(MemoryCard, medium ? 1 : 0);
        Grid.SetRow(WindowsCard, wide ? 0 : medium ? 1 : 2);
        Grid.SetColumn(WindowsCard, wide ? 2 : 0);
        Grid.SetRow(ModeCard, wide ? 0 : medium ? 1 : 3);
        Grid.SetColumn(ModeCard, wide ? 3 : medium ? 1 : 0);

        WorkspaceColumn1.Width = medium ? star : hidden;
        Grid.SetRow(LauncherCard, medium ? 0 : 1);
        Grid.SetColumn(LauncherCard, medium ? 1 : 0);

        ConfigurationColumn0.Width = wide
            ? new GridLength(5, GridUnitType.Star)
            : star;
        ConfigurationColumn1.Width = wide
            ? new GridLength(7, GridUnitType.Star)
            : hidden;
        Grid.SetRow(PluginCard, wide ? 0 : 1);
        Grid.SetColumn(PluginCard, wide ? 1 : 0);
    }

    private void OnOpenLauncherClicked(object sender, RoutedEventArgs e)
    {
        LauncherRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnGamingModeToggled(object sender, RoutedEventArgs e)
    {
        if (!_applyingSettings)
        {
            ManualGamingModeChanged?.Invoke(GamingModeToggle.IsOn);
        }
    }

    private void OnAutomaticGamingModeToggled(object sender, RoutedEventArgs e)
    {
        if (!_applyingSettings)
        {
            AutomaticGamingModeChanged?.Invoke(AutomaticGamingModeToggle.IsOn);
        }
    }

    private void OnSaveGameRulesClicked(object sender, RoutedEventArgs e)
    {
        GameProcessRulesSaved?.Invoke(GameProcessRulesTextBox.Text);
    }

    private void OnPluginEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_applyingPluginDiagnostics ||
            sender is not ToggleSwitch toggle ||
            toggle.Tag is not string pluginId ||
            _pendingPluginIds.Contains(pluginId))
        {
            return;
        }

        _pendingPluginIds.Add(pluginId);
        ApplyPluginDiagnostics();
        if (PluginEnabledChanged is null)
        {
            _pendingPluginIds.Remove(pluginId);
            ApplyPluginDiagnostics();
            return;
        }

        PluginEnabledChanged.Invoke(pluginId, toggle.IsOn);
    }

    private void OnDockAutoHideToggled(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }

        DockAutoHideChanged?.Invoke(DockAutoHideToggle.IsOn);
        UpdateDockStatus(_shellState.Current.Mode == ShellMode.Gaming);
    }

    private void OnLauncherShortcutSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || LauncherShortcutComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (Enum.TryParse<LauncherShortcut>(item.Tag?.ToString(), out var shortcut))
        {
            LauncherShortcutChanged?.Invoke(shortcut);
        }
    }

    private void OnThemePreferenceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || ThemePreferenceComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (Enum.TryParse<ShellThemePreference>(item.Tag?.ToString(), out var theme))
        {
            ThemePreferenceChanged?.Invoke(theme);
        }
    }

    private void OnDisplayDensitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || DisplayDensityComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (Enum.TryParse<ShellDisplayDensity>(item.Tag?.ToString(), out var density))
        {
            DisplayDensityChanged?.Invoke(density);
        }
    }

    private void OnShellStateChanged(object? sender, ShellState state)
    {
        ApplyShellState(state);
        if (state.Mode == ShellMode.Gaming)
        {
            _refreshTimer.Stop();
            return;
        }

        _refreshTimer.Start();
        _ = RefreshDashboardAsync();
    }

    private void OnGamingModeStatusChanged(object? sender, GamingModeStatus status)
    {
        ApplyGamingModeStatus(status);
    }

    private void OnGamingDetectionPerformanceChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyGamingDetectionPerformance);
    }

    private void OnGamingSessionsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyGamingSessionHistory);
    }

    private void OnPluginDiagnosticsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyPluginDiagnostics);
    }

    private void ApplyShellState(ShellState state)
    {
        var gaming = state.Mode == ShellMode.Gaming;
        ModeText.Text = gaming ? "Gaming" : "Normal";
        ProviderStatus.Text = gaming ? "Providers paused" : "Providers active";
        UpdateDockStatus(gaming);
    }

    private void ApplyGamingModeStatus(GamingModeStatus status)
    {
        _applyingSettings = true;
        GamingModeToggle.IsOn = status.ManualModeEnabled;
        AutomaticGamingModeToggle.IsOn = status.AutomaticDetectionEnabled;
        _applyingSettings = false;

        GamingDetectionStatus.Text = status.ManualModeEnabled
            ? status.ActiveGameNames.Count > 0
                ? $"Manual mode active; also detected: {string.Join(", ", status.ActiveGameNames)}"
                : "Manual gaming mode is active"
            : status.ActiveGameNames.Count > 0
                ? $"Automatically detected: {string.Join(", ", status.ActiveGameNames)}"
                : status.AutomaticDetectionEnabled
                    ? status.ConfiguredRuleCount > 0
                        ? "Watching configured game processes"
                        : "Automatic detection is on; add at least one process name"
                    : "Automatic detection is off";
    }

    private void ApplyGamingDetectionPerformance()
    {
        var snapshot = _gamingDetectionPerformance.Current;
        GamingDetectionPerformance.Text = snapshot.SampleCount == 0
            ? "Detector performance: Not measured"
            : $"Detector: {snapshot.EstimatedCpuPercentage!.Value:F3}% estimated CPU · " +
              $"{snapshot.LastScanDuration!.Value.TotalMilliseconds:F1} ms last · " +
              $"{snapshot.P95ScanDuration!.Value.TotalMilliseconds:F1} ms P95 " +
              $"({snapshot.SampleCount} samples)";
    }

    private void ApplyGamingSessionHistory()
    {
        var history = _gamingSessions.Current;
        if (history.ActiveSessionStartedAt is not null)
        {
            GamingSessionHistory.Text =
                $"Recording detected session since {history.ActiveSessionStartedAt.Value.ToLocalTime():t}";
            return;
        }

        var latest = history.RecentSessions.FirstOrDefault();
        if (latest is null)
        {
            GamingSessionHistory.Text = history.Warning is null
                ? "Compatibility evidence: No detected session recorded yet"
                : $"Compatibility evidence unavailable: {history.Warning}";
            return;
        }

        var duration = latest.Duration.TotalHours >= 1
            ? $"{latest.Duration.TotalHours:F1} h"
            : $"{Math.Max(1, latest.Duration.TotalMinutes):F0} min";
        var performance = latest.EstimatedDetectorCpuPercentage is null ||
                          latest.DetectorP95Milliseconds is null
            ? "detector metrics unavailable"
            : $"{latest.EstimatedDetectorCpuPercentage.Value:F3}% CPU · " +
              $"{latest.DetectorP95Milliseconds.Value:F1} ms P95";
        GamingSessionHistory.Text =
            $"Last session: {string.Join(", ", latest.GameNames)} · {duration} · {performance}";
    }

    private void ApplyPluginDiagnostics()
    {
        var diagnostics = _pluginHost.Diagnostics;
        _applyingPluginDiagnostics = true;
        try
        {
            PluginDiagnosticsList.ItemsSource = diagnostics
                .Select(diagnostic => new PluginDiagnosticViewModel(
                    diagnostic,
                    !_pendingPluginIds.Contains(diagnostic.Id)))
                .ToArray();
        }
        finally
        {
            _applyingPluginDiagnostics = false;
        }

        var active = diagnostics.Count(static diagnostic => diagnostic.State == PluginRuntimeState.Active);
        var suspended = diagnostics.Count(static diagnostic => diagnostic.State == PluginRuntimeState.Suspended);
        var faulted = diagnostics.Count(static diagnostic => diagnostic.State == PluginRuntimeState.Faulted);
        var disabled = diagnostics.Count(static diagnostic => !diagnostic.IsEnabled);
        PluginStatusSummary.Text = diagnostics.Count == 0
            ? "No built-in plugins are registered"
            : $"{diagnostics.Count} registered · {active} active · {suspended} suspended · {disabled} disabled · {faulted} faulted";
        PluginEmptyState.Visibility = diagnostics.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PluginDiagnosticsList.Visibility = diagnostics.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task<bool> RefreshExternalPluginCandidatesAsync()
    {
        if (_refreshingExternalPlugins)
        {
            return false;
        }

        _refreshingExternalPlugins = true;
        RefreshExternalPluginsButton.IsEnabled = false;
        ExternalPluginStatusSummary.Text = "Rechecking package signatures and publisher revocation status";
        try
        {
            var candidates = await _externalPlugins.ScanAsync().ConfigureAwait(true);
            _externalPluginCandidates = candidates;
            ApplyExternalPluginCandidates();
            ExternalPluginCandidateList.Visibility =
                candidates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ExternalPluginEmptyState.Visibility = Visibility.Collapsed;

            UpdateExternalPluginStatusSummary();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExternalPluginCandidateList.Visibility = Visibility.Collapsed;
            ExternalPluginEmptyState.Visibility = Visibility.Visible;
            ExternalPluginEmptyState.Text = $"Candidate scan unavailable: {exception.Message}";
            ExternalPluginStatusSummary.Text = "External loading blocked";
            return false;
        }
        finally
        {
            _refreshingExternalPlugins = false;
            RefreshExternalPluginsButton.IsEnabled = true;
        }
    }

    private async void OnRefreshExternalPluginsClicked(object sender, RoutedEventArgs e)
    {
        if (!await RefreshExternalPluginCandidatesAsync().ConfigureAwait(true))
        {
            return;
        }

        SetSettingsStatus(
            "External plugin trust rechecked",
            "Signatures and publisher revocation status were checked again. External execution remains blocked.",
            InfoBarSeverity.Success);
    }

    private void ApplyExternalPluginCandidates()
    {
        ExternalPluginCandidateList.ItemsSource = _externalPluginCandidates
            .Select(candidate => new ExternalPluginCandidateViewModel(
                candidate,
                _externalPluginTrust.IsApproved(candidate),
                candidate.Id is null || !_pendingExternalPluginIds.Contains(candidate.Id)))
            .ToArray();
        ClearExternalPluginTrustButton.Visibility = _externalPluginTrust.Consents.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateExternalPluginStatusSummary()
    {
        var ready = _externalPluginCandidates.Count(
            static candidate => candidate.Status == ExternalPluginCandidateStatus.ReadyForConsent);
        ExternalPluginStatusSummary.Text = _externalPluginCandidates.Count == 0
            ? $"No external packages detected · {_externalPluginTrust.Consents.Count} stored approvals"
            : $"{_externalPluginCandidates.Count} detected · {ready} passed trust checks · " +
              $"{_externalPluginTrust.Consents.Count} stored approvals · loading blocked";
    }

    private async void OnClearExternalPluginTrustClicked(object sender, RoutedEventArgs e)
    {
        if (_externalPluginTrust.Consents.Count == 0)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Revoke all external plugin consent?",
            Content =
                "Every stored publisher and capability approval will be removed, including decisions for packages that are no longer installed.",
            PrimaryButtonText = "Revoke all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ClearExternalPluginTrustButton.IsEnabled = false;
        if (ExternalPluginTrustClearRequested is null)
        {
            ClearExternalPluginTrustButton.IsEnabled = true;
            return;
        }

        ExternalPluginTrustClearRequested.Invoke();
        ClearExternalPluginTrustButton.IsEnabled = true;
    }

    private async void OnExternalPluginConsentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ExternalPluginCandidate candidate } button ||
            candidate.Id is null ||
            _pendingExternalPluginIds.Contains(candidate.Id))
        {
            return;
        }

        var approved = !_externalPluginTrust.IsApproved(candidate);
        if (approved)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Approve external plugin capabilities?",
                Content =
                    $"{candidate.Name}\n" +
                    $"Publisher: {candidate.Publisher}\n" +
                    $"Certificate SHA-256: {candidate.SignerCertificateSha256}\n" +
                    $"Capabilities: {ExternalPluginCandidateViewModel.FormatCapabilities(candidate.Capabilities)}\n\n" +
                    "This records consent only. SeanShell will not load or execute the plugin until an isolated broker ships.",
                PrimaryButtonText = "Approve",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        else
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Revoke this plugin consent?",
                Content =
                    $"{candidate.Name} will lose its stored publisher and capability approval. External execution is already blocked.",
                PrimaryButtonText = "Revoke",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        _pendingExternalPluginIds.Add(candidate.Id);
        button.IsEnabled = false;
        ApplyExternalPluginCandidates();
        if (ExternalPluginConsentChanged is null)
        {
            _pendingExternalPluginIds.Remove(candidate.Id);
            ApplyExternalPluginCandidates();
            return;
        }

        ExternalPluginConsentChanged.Invoke(candidate, approved);
    }

    private void OnLauncherPerformanceChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyLauncherPerformance);
    }

    private void ApplyLauncherPerformance()
    {
        var snapshot = _launcherPerformance.Current;
        LauncherFirstUsablePerformance.Text = snapshot.FirstUsableDuration is null
            ? "First usable: Not measured · target < 300 ms"
            : $"First usable: {snapshot.FirstUsableDuration.Value.TotalMilliseconds:F0} ms · target < 300 ms";
        LauncherSearchPerformance.Text = snapshot.LastSearchDuration is null
            ? "Cached search P95: Not measured · target < 50 ms"
            : $"Cached search: {snapshot.LastSearchDuration.Value.TotalMilliseconds:F0} ms last · " +
              $"{snapshot.P95SearchDuration!.Value.TotalMilliseconds:F0} ms P95 " +
              $"({snapshot.SuccessfulSearchCount} samples) · target < 50 ms";
    }

    private void UpdateDockStatus(bool gaming)
    {
        DockStatus.Text = gaming
            ? $"Hidden during gaming mode on {_displayCount} display(s)"
            : DockAutoHideToggle.IsOn
                ? $"Auto-hide active on {_displayCount} display(s)"
                : $"Expanded on {_displayCount} display(s)";
    }

    private async void OnRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        await RefreshDashboardAsync().ConfigureAwait(true);
    }

    private async Task RefreshDashboardAsync()
    {
        if (_refreshing || _shellState.Current.Mode == ShellMode.Gaming)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var metrics = _systemMetrics.Capture();
                var windowCount = _desktopWindows.Capture().Count;
                return (metrics, windowCount);
            }).ConfigureAwait(true);

            CpuValue.Text = $"{snapshot.metrics.CpuUsagePercent:F0}%";
            CpuProgress.Value = snapshot.metrics.CpuUsagePercent;
            MemoryValue.Text = $"{FormatGiB(snapshot.metrics.UsedPhysicalMemoryBytes)} / {FormatGiB(snapshot.metrics.TotalPhysicalMemoryBytes)}";
            MemoryProgress.Value = snapshot.metrics.MemoryUsagePercent;
            WindowCountValue.Text = snapshot.windowCount.ToString();
            DashboardStatus.Severity = InfoBarSeverity.Informational;
            DashboardStatus.Title = "Compatibility-first";
            DashboardStatus.Message = "Explorer remains active; the dock only enumerates supported top-level windows.";
        }
        catch (Exception exception)
        {
            DashboardStatus.Severity = InfoBarSeverity.Warning;
            DashboardStatus.Title = "Dashboard update paused";
            DashboardStatus.Message = exception.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static string FormatGiB(ulong bytes) => $"{bytes / 1_073_741_824d:F1} GB";

    private void ApplySettings(ShellSettings settings)
    {
        _applyingSettings = true;
        DockAutoHideToggle.IsOn = settings.DockAutoHide;
        AutomaticGamingModeToggle.IsOn = settings.AutomaticGamingModeEnabled;
        GameProcessRulesTextBox.Text = settings.GameProcessRules;
        SelectShortcut(settings.LauncherShortcut);
        SelectThemePreference(settings.Theme);
        SelectDisplayDensity(settings.DisplayDensity);
        ShortcutStatus.Text = $"Keyboard shortcut: {settings.LauncherShortcut.GetDisplayName()}";
        _applyingSettings = false;
    }

    private void ApplyDisplayDensity(ShellDisplayDensity density)
    {
        if (density != ShellDisplayDensity.Compact)
        {
            return;
        }

        DashboardRoot.Padding = new Thickness(20);
        DashboardSections.Spacing = 20;
        HeroCard.Padding = new Thickness(16);
        MetricsGrid.ColumnSpacing = 8;
        MetricsGrid.RowSpacing = 8;
        WorkspaceGrid.ColumnSpacing = 12;
        WorkspaceGrid.RowSpacing = 12;
        ConfigurationGrid.ColumnSpacing = 12;
        ConfigurationGrid.RowSpacing = 12;
        PluginCard.Padding = new Thickness(16);
        GamingModeCard.Padding = new Thickness(16);
    }

    private void SelectDisplayDensity(ShellDisplayDensity density)
    {
        var wasApplyingSettings = _applyingSettings;
        _applyingSettings = true;
        DisplayDensityComboBox.SelectedItem = DisplayDensityComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag?.ToString(), density.ToString(), StringComparison.Ordinal));
        _applyingSettings = wasApplyingSettings;
    }

    private void SelectThemePreference(ShellThemePreference theme)
    {
        var wasApplyingSettings = _applyingSettings;
        _applyingSettings = true;
        ThemePreferenceComboBox.SelectedItem = ThemePreferenceComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag?.ToString(), theme.ToString(), StringComparison.Ordinal));
        _applyingSettings = wasApplyingSettings;
    }

    private void SelectShortcut(LauncherShortcut shortcut)
    {
        var wasApplyingSettings = _applyingSettings;
        _applyingSettings = true;
        LauncherShortcutComboBox.SelectedItem = LauncherShortcutComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag?.ToString(), shortcut.ToString(), StringComparison.Ordinal));
        _applyingSettings = wasApplyingSettings;
    }

    private void SetSettingsStatus(string title, string message, InfoBarSeverity severity)
    {
        SettingsStatus.Title = title;
        SettingsStatus.Message = message;
        SettingsStatus.Severity = severity;
        SettingsStatus.IsOpen = true;
    }
}
