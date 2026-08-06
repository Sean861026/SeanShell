namespace SeanShell.Core;

public enum TaskbarClickAction
{
    Default,
    OpenNewInstance,
    OpenElevatedInstance,
}

public static class TaskbarClickActionResolver
{
    public static TaskbarClickAction Resolve(
        bool shiftPressed,
        bool controlPressed) =>
        (shiftPressed, controlPressed) switch
        {
            (true, true) => TaskbarClickAction.OpenElevatedInstance,
            (true, false) => TaskbarClickAction.OpenNewInstance,
            _ => TaskbarClickAction.Default,
        };
}
