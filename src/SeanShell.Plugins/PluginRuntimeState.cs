namespace SeanShell.Plugins;

public enum PluginRuntimeState
{
    NotInitialized,
    Disabled,
    Active,
    Suspended,
    Faulted,
    Disposed,
}
