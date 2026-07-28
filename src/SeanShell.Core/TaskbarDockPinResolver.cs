namespace SeanShell.Core;

public static class TaskbarDockPinResolver
{
    public static ShellCommand? FindPinnedApplication(
        IEnumerable<ShellCommand> pinnedApplications,
        IReadOnlyList<DesktopWindowSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(pinnedApplications);
        ArgumentNullException.ThrowIfNull(windows);

        return pinnedApplications.FirstOrDefault(
            application => windows.Any(
                window => TaskbarPinWindowMatcher.IsMatch(application, window)));
    }

    public static IReadOnlyList<ShellCommand> FindPinCandidates(
        IEnumerable<ShellCommand> availableApplications,
        IReadOnlyList<DesktopWindowSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(availableApplications);
        ArgumentNullException.ThrowIfNull(windows);

        return availableApplications
            .Where(static application =>
                application.Kind == ShellCommandKind.Application)
            .Where(application => windows.Any(
                window => TaskbarPinWindowMatcher.IsMatch(application, window)))
            .DistinctBy(
                static application => application.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
