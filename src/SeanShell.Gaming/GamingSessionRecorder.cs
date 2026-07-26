namespace SeanShell.Gaming;

public sealed class GamingSessionRecorder
{
    private const int SessionCapacity = 20;
    private readonly HashSet<string> _activeGameNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly string _seanShellVersion;
    private readonly GamingSessionStore _store;
    private readonly string _windowsVersion;
    private readonly List<GamingSessionRecord> _recentSessions;
    private DateTimeOffset? _activeSessionStartedAt;
    private string? _warning;

    public GamingSessionRecorder(
        GamingSessionStore store,
        GamingSessionLoadResult loadResult,
        string windowsVersion,
        string seanShellVersion)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(loadResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(seanShellVersion);
        _windowsVersion = windowsVersion;
        _seanShellVersion = seanShellVersion;
        _warning = loadResult.Warning;
        _recentSessions = loadResult.Sessions
            .OrderByDescending(static session => session.EndedAt)
            .Take(SessionCapacity)
            .ToList();
    }

    public event EventHandler? Changed;

    public GamingSessionHistorySnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return new GamingSessionHistorySnapshot(
                    _activeSessionStartedAt,
                    _recentSessions.ToArray(),
                    _warning);
            }
        }
    }

    public GamingSessionTransition Observe(
        GamingModeStatus status,
        GamingDetectionPerformanceSnapshot performance,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(performance);
        var observedNames = status.ActiveGameNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        GamingSessionTransition transition;
        IReadOnlyList<GamingSessionRecord>? sessionsToSave = null;

        lock (_gate)
        {
            if (observedNames.Count > 0)
            {
                transition = ObserveActiveSession(observedNames, observedAt);
            }
            else if (_activeSessionStartedAt is not null)
            {
                transition = CompleteActiveSession(performance, observedAt);
                sessionsToSave = _recentSessions.ToArray();
            }
            else
            {
                return GamingSessionTransition.None;
            }
        }

        if (sessionsToSave is not null)
        {
            TrySave(sessionsToSave);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return transition;
    }

    private GamingSessionTransition ObserveActiveSession(
        IReadOnlySet<string> observedNames,
        DateTimeOffset observedAt)
    {
        if (_activeSessionStartedAt is null)
        {
            _activeSessionStartedAt = observedAt;
            _activeGameNames.UnionWith(observedNames);
            return GamingSessionTransition.Started;
        }

        var previousCount = _activeGameNames.Count;
        _activeGameNames.UnionWith(observedNames);
        return previousCount == _activeGameNames.Count
            ? GamingSessionTransition.None
            : GamingSessionTransition.Updated;
    }

    private GamingSessionTransition CompleteActiveSession(
        GamingDetectionPerformanceSnapshot performance,
        DateTimeOffset observedAt)
    {
        var startedAt = _activeSessionStartedAt!.Value;
        var record = new GamingSessionRecord(
            Guid.NewGuid(),
            startedAt,
            observedAt < startedAt ? startedAt : observedAt,
            _activeGameNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            performance.SampleCount,
            performance.EstimatedCpuPercentage,
            performance.P95ScanDuration?.TotalMilliseconds,
            _windowsVersion,
            _seanShellVersion);
        _recentSessions.Insert(0, record);
        if (_recentSessions.Count > SessionCapacity)
        {
            _recentSessions.RemoveRange(SessionCapacity, _recentSessions.Count - SessionCapacity);
        }

        _activeSessionStartedAt = null;
        _activeGameNames.Clear();
        return GamingSessionTransition.Completed;
    }

    private void TrySave(IReadOnlyList<GamingSessionRecord> sessions)
    {
        try
        {
            _store.Save(sessions);
            lock (_gate)
            {
                _warning = null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _warning = $"The latest gaming session is available now but could not be saved. {exception.Message}";
            }
        }
    }
}
