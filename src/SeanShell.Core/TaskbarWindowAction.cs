namespace SeanShell.Core;

public enum TaskbarWindowAction
{
    Minimize,
    RestoreAndActivate,
}

public static class TaskbarWindowActionResolver
{
    public static TaskbarWindowAction Resolve(
        bool isForeground,
        bool isMinimized) =>
        isForeground && !isMinimized
            ? TaskbarWindowAction.Minimize
            : TaskbarWindowAction.RestoreAndActivate;

    public static TaskbarWindowAction ResolveContextToggle(bool isMinimized) =>
        isMinimized
            ? TaskbarWindowAction.RestoreAndActivate
            : TaskbarWindowAction.Minimize;
}
