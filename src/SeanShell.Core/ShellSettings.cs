namespace SeanShell.Core;

public sealed record ShellSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool DockAutoHide { get; init; } = true;

    public LauncherShortcut LauncherShortcut { get; init; } = LauncherShortcut.AltSpace;

    public bool AutomaticGamingModeEnabled { get; init; }

    public string GameProcessRules { get; init; } = string.Empty;

    public string DisabledPluginIds { get; init; } = string.Empty;
}

public sealed record SettingsLoadResult(
    ShellSettings Settings,
    bool WasRecovered = false,
    string? Warning = null);
