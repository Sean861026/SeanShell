namespace SeanShell.Core;

public enum DockNavigationDirection
{
    Previous,
    Next,
    First,
    Last,
}

public static class DockKeyboardNavigation
{
    public static int Move(int count, int selectedIndex, DockNavigationDirection direction)
    {
        if (count <= 0 || !Enum.IsDefined(direction))
        {
            return -1;
        }

        if (direction == DockNavigationDirection.First)
        {
            return 0;
        }

        if (direction == DockNavigationDirection.Last)
        {
            return count - 1;
        }

        if (selectedIndex < 0 || selectedIndex >= count)
        {
            return direction == DockNavigationDirection.Previous ? count - 1 : 0;
        }

        return direction == DockNavigationDirection.Previous
            ? selectedIndex == 0 ? count - 1 : selectedIndex - 1
            : selectedIndex == count - 1 ? 0 : selectedIndex + 1;
    }

    public static int ResolveRefreshIndex(
        IReadOnlyList<string> groupKeys,
        string? selectedGroupKey,
        int previousIndex,
        bool keyboardFocusInList,
        int foregroundIndex)
    {
        ArgumentNullException.ThrowIfNull(groupKeys);
        if (groupKeys.Count == 0)
        {
            return -1;
        }

        if (!keyboardFocusInList)
        {
            return foregroundIndex >= 0 && foregroundIndex < groupKeys.Count ? foregroundIndex : -1;
        }

        if (selectedGroupKey is not null)
        {
            for (var index = 0; index < groupKeys.Count; index++)
            {
                if (string.Equals(groupKeys[index], selectedGroupKey, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        // A closed group falls back to the nearest remaining slot, not an
        // unrelated foreground group that changed during keyboard navigation.
        return Math.Clamp(previousIndex, 0, groupKeys.Count - 1);
    }
}
