namespace SeanShell.Core;

public sealed record ShellSettings
{
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool DockAutoHide { get; init; } = true;

    public bool ReplaceWindowsTaskbar { get; init; }

    public string PinnedApplicationIds { get; init; } = string.Empty;

    public LauncherShortcut LauncherShortcut { get; init; } = LauncherShortcut.AltSpace;

    public DockShortcut DockShortcut { get; init; } = DockShortcut.ControlAltD;

    public ShellThemePreference Theme { get; init; } = ShellThemePreference.System;

    public ShellDisplayDensity DisplayDensity { get; init; } = ShellDisplayDensity.Comfortable;

    public bool AutomaticGamingModeEnabled { get; init; }

    public string GameProcessRules { get; init; } = string.Empty;

    public string DisabledPluginIds { get; init; } = string.Empty;
}

public sealed record SettingsLoadResult(
    ShellSettings Settings,
    bool WasRecovered = false,
    string? Warning = null);
