namespace SeanShell.Core;

public enum TaskbarItemIndicator
{
    Running,
    Active,
    Minimized,
}

public readonly record struct TaskbarItemVisualState(
    TaskbarItemIndicator Indicator,
    double ContentOpacity);

public static class TaskbarItemVisualStateResolver
{
    public static TaskbarItemVisualState Resolve(
        bool isForeground,
        bool isMinimized)
    {
        if (isForeground)
        {
            return new(TaskbarItemIndicator.Active, 1);
        }

        return isMinimized
            ? new(TaskbarItemIndicator.Minimized, 0.72)
            : new(TaskbarItemIndicator.Running, 1);
    }
}
