namespace SeanShell.Core;

public static class TaskbarWindowCycleResolver
{
    public static int ResolveNextIndex(
        IReadOnlyList<bool> foregroundStates)
    {
        ArgumentNullException.ThrowIfNull(foregroundStates);
        if (foregroundStates.Count == 0)
        {
            return -1;
        }

        for (var index = 0; index < foregroundStates.Count; index++)
        {
            if (foregroundStates[index])
            {
                return (index + 1) % foregroundStates.Count;
            }
        }

        return 0;
    }
}
