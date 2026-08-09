namespace SeanShell.Core;

public enum TaskbarWindowMoveDirection
{
    Left,
    Right,
}

public static class TaskbarWindowOrder
{
    public static TaskbarWindowOrderResult Apply(
        IReadOnlyList<TaskbarWindowGroup> groups,
        IReadOnlyList<string> previousKeys)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(previousKeys);

        var groupsByKey = groups.ToDictionary(
            TaskbarWindowGrouper.GetKey,
            StringComparer.OrdinalIgnoreCase);
        var orderedKeys = previousKeys
            .Where(groupsByKey.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var knownKeys = orderedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var key = TaskbarWindowGrouper.GetKey(group);
            if (knownKeys.Add(key))
            {
                orderedKeys.Add(key);
            }
        }

        return new TaskbarWindowOrderResult(
            orderedKeys.Select(key => groupsByKey[key]).ToArray(),
            orderedKeys);
    }

    public static bool CanMove(
        IReadOnlyList<string> keys,
        string key,
        TaskbarWindowMoveDirection direction)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var index = FindIndex(keys, key);
        return direction switch
        {
            TaskbarWindowMoveDirection.Left => index > 0,
            TaskbarWindowMoveDirection.Right =>
                index >= 0 && index < keys.Count - 1,
            _ => false,
        };
    }

    public static IReadOnlyList<string> Move(
        IReadOnlyList<string> keys,
        string key,
        TaskbarWindowMoveDirection direction)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var reordered = keys.ToArray();
        var index = FindIndex(reordered, key);
        var targetIndex = direction switch
        {
            TaskbarWindowMoveDirection.Left when index > 0 => index - 1,
            TaskbarWindowMoveDirection.Right
                when index >= 0 && index < reordered.Length - 1 => index + 1,
            _ => index,
        };
        if (index < 0 || targetIndex == index)
        {
            return reordered;
        }

        (reordered[index], reordered[targetIndex]) =
            (reordered[targetIndex], reordered[index]);
        return reordered;
    }

    private static int FindIndex(IReadOnlyList<string> keys, string key)
    {
        for (var index = 0; index < keys.Count; index++)
        {
            if (string.Equals(keys[index], key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed record TaskbarWindowOrderResult(
    IReadOnlyList<TaskbarWindowGroup> Groups,
    IReadOnlyList<string> Keys);
