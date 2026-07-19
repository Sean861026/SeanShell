namespace SeanShell.Core;

public sealed record ShellSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool DockAutoHide { get; init; } = true;

    public LauncherShortcut LauncherShortcut { get; init; } = LauncherShortcut.AltSpace;
}

public sealed record SettingsLoadResult(
    ShellSettings Settings,
    bool WasRecovered = false,
    string? Warning = null);
