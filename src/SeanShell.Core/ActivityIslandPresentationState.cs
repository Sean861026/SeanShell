namespace SeanShell.Core;

public enum ActivityIslandPresentationChange
{
    Show,
    Update,
    Unchanged,
}

/// <summary>
/// UI-thread-owned, single-message presentation lifetime. Callers supply
/// monotonic milliseconds; the state never schedules work or retains a queue.
/// </summary>
public sealed class ActivityIslandPresentationState
{
    public const int DismissDelayMilliseconds = 2400;

    private ActivityIslandContent? _content;
    private nint _monitorHandle;
    private long _dismissAt;

    public ActivityIslandPresentationChange Present(
        ActivityIslandContent content,
        nint monitorHandle,
        long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);
        if (monitorHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorHandle));
        }

        var isVisible = RemainingMilliseconds(nowMilliseconds) > 0;
        if (isVisible && _monitorHandle == monitorHandle && _content == content)
        {
            // Identical snapshots must not keep the capsule alive indefinitely.
            return ActivityIslandPresentationChange.Unchanged;
        }

        var change = isVisible && _monitorHandle == monitorHandle
            ? ActivityIslandPresentationChange.Update
            : ActivityIslandPresentationChange.Show;
        _content = content;
        _monitorHandle = monitorHandle;
        _dismissAt = nowMilliseconds > long.MaxValue - DismissDelayMilliseconds
            ? long.MaxValue
            : nowMilliseconds + DismissDelayMilliseconds;
        return change;
    }

    public int RemainingMilliseconds(long nowMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);
        if (_content is null || nowMilliseconds >= _dismissAt)
        {
            return 0;
        }

        // The cap is defensive: a caller querying an earlier timestamp cannot
        // turn the fixed lifetime into an unexpectedly long timer interval.
        return (int)Math.Min(DismissDelayMilliseconds, _dismissAt - nowMilliseconds);
    }

    public void Dismiss()
    {
        _content = null;
        _monitorHandle = 0;
        _dismissAt = 0;
    }
}
