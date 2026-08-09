namespace SeanShell.Core;

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
}

public sealed record TaskbarWindowOrderResult(
    IReadOnlyList<TaskbarWindowGroup> Groups,
    IReadOnlyList<string> Keys);
