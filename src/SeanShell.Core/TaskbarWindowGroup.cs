namespace SeanShell.Core;

public sealed record TaskbarWindowGroup(
    string ProcessName,
    IReadOnlyList<DesktopWindowSnapshot> Windows)
{
    public DesktopWindowSnapshot PrimaryWindow =>
        Windows.FirstOrDefault(static window => window.IsForeground) ??
        Windows.First();

    public bool IsForeground =>
        Windows.Any(static window => window.IsForeground);

    public bool IsMinimized =>
        Windows.All(static window => window.IsMinimized);
}
