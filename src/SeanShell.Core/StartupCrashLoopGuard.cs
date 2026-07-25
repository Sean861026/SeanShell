using System.Text.Json;

namespace SeanShell.Core;

public sealed class StartupCrashLoopGuard
{
    public const int FailureThreshold = 3;

    private readonly string _filePath;
    private readonly string _temporaryPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public StartupCrashLoopGuard(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _temporaryPath = $"{_filePath}.tmp";
    }

    public StartupSessionResult BeginSession(bool automaticStartup)
    {
        var load = Load();
        var state = load.State;
        var consecutiveFailures = state.StartupPending
            ? Math.Min(state.ConsecutiveFailures + 1, FailureThreshold)
            : state.ConsecutiveFailures;

        if (automaticStartup &&
            (state.AutomaticStartupDisabled || consecutiveFailures >= FailureThreshold))
        {
            var blockedState = state with
            {
                StartupPending = false,
                CurrentSessionId = null,
                ConsecutiveFailures = consecutiveFailures,
                AutomaticStartupDisabled = true,
            };
            if (!TrySave(blockedState, out var saveError))
            {
                return StartupSessionResult.Blocked(
                    consecutiveFailures,
                    CombineWarnings(load.Warning, saveError));
            }

            return StartupSessionResult.Blocked(consecutiveFailures, load.Warning);
        }

        var sessionId = Guid.NewGuid();
        var startingState = state with
        {
            CurrentSessionId = sessionId,
            StartupPending = true,
            ConsecutiveFailures = consecutiveFailures,
        };
        if (!TrySave(startingState, out var error))
        {
            if (automaticStartup)
            {
                return StartupSessionResult.Blocked(
                    consecutiveFailures,
                    CombineWarnings(load.Warning, error));
            }

            return StartupSessionResult.Allowed(
                sessionId,
                consecutiveFailures,
                state.AutomaticStartupDisabled,
                CombineWarnings(load.Warning, error));
        }

        return StartupSessionResult.Allowed(
            sessionId,
            consecutiveFailures,
            state.AutomaticStartupDisabled,
            load.Warning);
    }

    public bool MarkHealthy(Guid sessionId) => CompleteSession(sessionId);

    public bool MarkCleanExit(Guid sessionId) => CompleteSession(sessionId);

    private bool CompleteSession(Guid sessionId)
    {
        var load = Load();
        if (!load.State.StartupPending || load.State.CurrentSessionId != sessionId)
        {
            return false;
        }

        return TrySave(
            load.State with
            {
                CurrentSessionId = null,
                StartupPending = false,
                ConsecutiveFailures = 0,
                AutomaticStartupDisabled = false,
            },
            out _);
    }

    private StartupHealthLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new(new StartupHealthState());
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            var state = JsonSerializer.Deserialize<StartupHealthState>(stream, _jsonOptions);
            if (state is null || state.SchemaVersion != StartupHealthState.CurrentSchemaVersion)
            {
                throw new InvalidDataException("The startup health document is not supported.");
            }

            return new(state);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new(
                new StartupHealthState(),
                $"Startup health history could not be loaded. {exception.Message}");
        }
    }

    private bool TrySave(StartupHealthState state, out string? error)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("The startup health path must include a directory.");
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(
                _temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state, _jsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(_temporaryPath, _filePath, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = $"Startup health history could not be saved. {exception.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(_temporaryPath))
                {
                    File.Delete(_temporaryPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? CombineWarnings(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        return string.IsNullOrWhiteSpace(second) ? first : $"{first} {second}";
    }

    private sealed record StartupHealthLoadResult(
        StartupHealthState State,
        string? Warning = null);
}

public sealed record StartupHealthState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Guid? CurrentSessionId { get; init; }

    public bool StartupPending { get; init; }

    public int ConsecutiveFailures { get; init; }

    public bool AutomaticStartupDisabled { get; init; }
}

public sealed record StartupSessionResult(
    bool CanStart,
    Guid? SessionId,
    int ConsecutiveFailures,
    bool AutomaticStartupDisabled,
    string? Warning)
{
    internal static StartupSessionResult Allowed(
        Guid sessionId,
        int consecutiveFailures,
        bool automaticStartupDisabled,
        string? warning) =>
        new(true, sessionId, consecutiveFailures, automaticStartupDisabled, warning);

    internal static StartupSessionResult Blocked(
        int consecutiveFailures,
        string? warning) =>
        new(false, null, consecutiveFailures, true, warning);
}
