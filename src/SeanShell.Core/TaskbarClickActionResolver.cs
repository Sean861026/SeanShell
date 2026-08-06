namespace SeanShell.Core;

public enum TaskbarClickAction
{
    Default,
    OpenNewInstance,
}

public static class TaskbarClickActionResolver
{
    public static TaskbarClickAction Resolve(bool shiftPressed) =>
        shiftPressed
            ? TaskbarClickAction.OpenNewInstance
            : TaskbarClickAction.Default;
}
