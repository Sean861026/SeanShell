namespace SeanShell.Core;

public enum TaskbarWindowGroupAction
{
    None,
    MinimizeAll,
    RestoreAll,
}

public static class TaskbarWindowGroupActionResolver
{
    public static TaskbarWindowGroupAction Resolve(
        IReadOnlyCollection<bool> minimizedStates)
    {
        ArgumentNullException.ThrowIfNull(minimizedStates);
        if (minimizedStates.Count == 0)
        {
            return TaskbarWindowGroupAction.None;
        }

        return minimizedStates.All(static isMinimized => isMinimized)
            ? TaskbarWindowGroupAction.RestoreAll
            : TaskbarWindowGroupAction.MinimizeAll;
    }
}
