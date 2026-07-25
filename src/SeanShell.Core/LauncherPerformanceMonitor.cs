namespace SeanShell.Core;

public sealed class LauncherPerformanceMonitor
{
    private const int SearchSampleCapacity = 50;
    private readonly object _gate = new();
    private readonly Queue<TimeSpan> _searchDurations = new();
    private TimeSpan? _firstUsableDuration;

    public event EventHandler? Changed;

    public LauncherPerformanceSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshot();
            }
        }
    }

    public void RecordFirstUsable(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        lock (_gate)
        {
            if (_firstUsableDuration is not null)
            {
                return;
            }

            _firstUsableDuration = duration;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RecordSuccessfulSearch(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        lock (_gate)
        {
            _searchDurations.Enqueue(duration);
            if (_searchDurations.Count > SearchSampleCapacity)
            {
                _searchDurations.Dequeue();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private LauncherPerformanceSnapshot CreateSnapshot()
    {
        if (_searchDurations.Count == 0)
        {
            return new LauncherPerformanceSnapshot(_firstUsableDuration, null, null, 0);
        }

        var ordered = _searchDurations.Order().ToArray();
        var percentileIndex = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
        return new LauncherPerformanceSnapshot(
            _firstUsableDuration,
            _searchDurations.Last(),
            ordered[percentileIndex],
            _searchDurations.Count);
    }
}
