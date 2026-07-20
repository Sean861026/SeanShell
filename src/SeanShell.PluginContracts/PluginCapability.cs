namespace SeanShell.PluginContracts;

[Flags]
public enum PluginCapability
{
    None = 0,
    LauncherCommands = 1 << 0,
    BackgroundWork = 1 << 1,
}
