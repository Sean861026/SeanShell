namespace SeanShell.Plugins;

public sealed record PluginHostOptions(
    TimeSpan InitializationTimeout,
    TimeSpan CommandQueryTimeout,
    TimeSpan LifecycleTimeout)
{
    public static PluginHostOptions Default { get; } = new(
        TimeSpan.FromSeconds(3),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(2));

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(InitializationTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(CommandQueryTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(LifecycleTimeout, TimeSpan.Zero);
    }
}
