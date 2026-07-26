namespace SeanShell.Plugins;

public sealed class PluginBrokerQuarantineManager
{
    public const int FailureThreshold = 3;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan QuarantineDuration = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();
    private readonly PluginBrokerQuarantineStore _store;
    private readonly TimeProvider _timeProvider;
    private PluginBrokerQuarantineDocument _document;
    private bool _persistenceAvailable;

    public PluginBrokerQuarantineManager(
        PluginBrokerQuarantineStore store,
        PluginBrokerQuarantineLoadResult loadResult,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loadResult);
        _store = store;
        _document = loadResult.Document;
        _persistenceAvailable = loadResult.PersistenceAvailable;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Warning = loadResult.Warning;
    }

    public string? Warning { get; private set; }

    public IReadOnlyList<PluginBrokerQuarantineStatus> Statuses
    {
        get
        {
            lock (_gate)
            {
                return _document.EffectiveEntries
                    .Select(static entry => new PluginBrokerQuarantineStatus(
                        entry.PluginId,
                        entry.ConsecutiveFailures,
                        entry.QuarantinedUntilUtc))
                    .ToArray();
            }
        }
    }

    public void EnsureProbeAllowed(string pluginId)
    {
        ValidatePluginId(pluginId);
        lock (_gate)
        {
            EnsurePersistenceAvailable();
            var now = _timeProvider.GetUtcNow();
            var entry = Find(pluginId);
            if (entry is null)
            {
                return;
            }

            if (entry.QuarantinedUntilUtc > now)
            {
                throw new PluginBrokerQuarantinedException(
                    entry.PluginId,
                    entry.QuarantinedUntilUtc.Value);
            }

            if (entry.QuarantinedUntilUtc is not null ||
                now - entry.WindowStartedAtUtc > FailureWindow)
            {
                SaveThenApply(Remove(pluginId));
            }
        }
    }

    public PluginBrokerQuarantineStatus RecordFailure(string pluginId)
    {
        ValidatePluginId(pluginId);
        lock (_gate)
        {
            EnsurePersistenceAvailable();
            var now = _timeProvider.GetUtcNow();
            var existing = Find(pluginId);
            var withinWindow = existing is not null &&
                               existing.QuarantinedUntilUtc is null &&
                               now - existing.WindowStartedAtUtc <= FailureWindow;
            var failures = withinWindow
                ? Math.Min(existing!.ConsecutiveFailures + 1, FailureThreshold)
                : 1;
            var windowStartedAtUtc = withinWindow
                ? existing!.WindowStartedAtUtc
                : now;
            DateTimeOffset? quarantinedUntilUtc = failures >= FailureThreshold
                ? now + QuarantineDuration
                : null;
            var updated = new PluginBrokerQuarantineEntry(
                pluginId,
                failures,
                windowStartedAtUtc,
                now,
                quarantinedUntilUtc);
            var entries = _document.EffectiveEntries
                .Where(entry =>
                    !string.Equals(entry.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static entry => entry.LastFailureAtUtc)
                .ThenBy(static entry => entry.PluginId, StringComparer.OrdinalIgnoreCase)
                .Take(PluginBrokerQuarantineStore.MaximumEntries - 1)
                .Append(updated)
                .OrderBy(static entry => entry.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            SaveThenApply(new PluginBrokerQuarantineDocument(Entries: entries));
            return new(updated.PluginId, updated.ConsecutiveFailures, updated.QuarantinedUntilUtc);
        }
    }

    public void RecordSuccess(string pluginId)
    {
        ValidatePluginId(pluginId);
        lock (_gate)
        {
            EnsurePersistenceAvailable();
            if (Find(pluginId) is not null)
            {
                SaveThenApply(Remove(pluginId));
            }
        }
    }

    private PluginBrokerQuarantineEntry? Find(string pluginId) =>
        _document.EffectiveEntries.FirstOrDefault(entry =>
            string.Equals(entry.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));

    private PluginBrokerQuarantineDocument Remove(string pluginId) =>
        new(
            Entries: _document.EffectiveEntries
                .Where(entry =>
                    !string.Equals(entry.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                .ToArray());

    private void SaveThenApply(PluginBrokerQuarantineDocument document)
    {
        try
        {
            _store.Save(document);
            _document = document;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
        {
            _persistenceAvailable = false;
            Warning =
                $"Broker quarantine history could not be saved; external broker probes are blocked. {exception.Message}";
            throw new InvalidOperationException(Warning, exception);
        }
    }

    private void EnsurePersistenceAvailable()
    {
        if (!_persistenceAvailable)
        {
            throw new InvalidOperationException(
                Warning ??
                "Broker quarantine history is unavailable; external broker probes are blocked.");
        }
    }

    private static void ValidatePluginId(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (pluginId.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            throw new ArgumentException("The plugin ID is invalid.", nameof(pluginId));
        }
    }
}
