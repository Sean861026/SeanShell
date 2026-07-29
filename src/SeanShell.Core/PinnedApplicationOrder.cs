namespace SeanShell.Core;

public enum PinnedApplicationMoveDirection
{
    Left,
    Right,
}

public static class PinnedApplicationOrder
{
    public static IReadOnlyList<string> MergeVisibleOrder(
        IReadOnlyList<string> applicationIds,
        IReadOnlyList<string> visibleApplicationIds)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        ArgumentNullException.ThrowIfNull(visibleApplicationIds);

        var visibleSet = visibleApplicationIds.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        if (visibleSet.Count != visibleApplicationIds.Count ||
            visibleApplicationIds.Any(static id => string.IsNullOrWhiteSpace(id)) ||
            visibleSet.Any(id => !applicationIds.Contains(
                id,
                StringComparer.OrdinalIgnoreCase)))
        {
            return applicationIds.ToArray();
        }

        var expectedVisibleCount = applicationIds.Count(visibleSet.Contains);
        if (expectedVisibleCount != visibleApplicationIds.Count)
        {
            return applicationIds.ToArray();
        }

        var reordered = applicationIds.ToArray();
        var visibleIndex = 0;
        for (var index = 0; index < reordered.Length; index++)
        {
            if (visibleSet.Contains(reordered[index]))
            {
                reordered[index] = visibleApplicationIds[visibleIndex++];
            }
        }

        return reordered;
    }

    public static bool CanMove(
        IReadOnlyList<string> applicationIds,
        string applicationId,
        PinnedApplicationMoveDirection direction)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        var index = FindIndex(applicationIds, applicationId);
        return direction switch
        {
            PinnedApplicationMoveDirection.Left => index > 0,
            PinnedApplicationMoveDirection.Right =>
                index >= 0 && index < applicationIds.Count - 1,
            _ => false,
        };
    }

    public static IReadOnlyList<string> Move(
        IReadOnlyList<string> applicationIds,
        string applicationId,
        PinnedApplicationMoveDirection direction)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        var reordered = applicationIds.ToArray();
        var index = FindIndex(reordered, applicationId);
        var targetIndex = direction switch
        {
            PinnedApplicationMoveDirection.Left when index > 0 => index - 1,
            PinnedApplicationMoveDirection.Right
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

    private static int FindIndex(
        IReadOnlyList<string> applicationIds,
        string applicationId)
    {
        for (var index = 0; index < applicationIds.Count; index++)
        {
            if (string.Equals(
                    applicationIds[index],
                    applicationId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
