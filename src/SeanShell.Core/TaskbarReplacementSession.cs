namespace SeanShell.Core;

public interface ITaskbarController
{
    TaskbarOperationResult HideAll();

    TaskbarOperationResult ShowAll();
}

public interface ITaskbarRecoveryGuard
{
    bool EnsureStarted(out string? error);
}

public sealed record TaskbarOperationResult(
    bool Success,
    int TaskbarCount,
    string? Error = null,
    bool Superseded = false);

public static class TaskbarRecoveryArguments
{
    public const string GuardModeArgument = "--taskbar-recovery-guard";
    public const string ReadyMessage = "SEANSHELL_TASKBAR_GUARD_READY";

    public static bool IsRequested(string[] arguments) =>
        arguments is { Length: > 0 } &&
        string.Equals(
            arguments[0],
            GuardModeArgument,
            StringComparison.Ordinal);

    public static bool TryParseOwnerProcessId(
        string[] arguments,
        out int ownerProcessId)
    {
        ownerProcessId = 0;
        return arguments is { Length: 2 } &&
               string.Equals(
                   arguments[0],
                   GuardModeArgument,
                   StringComparison.Ordinal) &&
               int.TryParse(
                   arguments[1],
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out ownerProcessId) &&
               ownerProcessId > 0;
    }
}

public sealed class TaskbarReplacementSession : IAsyncDisposable
{
    private readonly ITaskbarController _controller;
    private readonly ITaskbarRecoveryGuard _guard;
    private readonly object _queueGate = new();
    private Task _tail = Task.CompletedTask;
    private Task<TaskbarOperationResult>? _shutdown;
    private long _revision;
    private bool _isEnabled;

    public TaskbarReplacementSession(
        ITaskbarController controller,
        ITaskbarRecoveryGuard guard)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(guard);
        _controller = controller;
        _guard = guard;
    }

    public bool IsEnabled => Volatile.Read(ref _isEnabled);

    public Task<TaskbarOperationResult> EnableAsync()
    {
        lock (_queueGate)
        {
            if (_shutdown is not null)
            {
                return Task.FromResult(SupersededResult());
            }

            var revision = ++_revision;
            return Enqueue(() => Hide(revision, enabling: true));
        }
    }

    public Task<TaskbarOperationResult> EnsureHiddenAsync()
    {
        lock (_queueGate)
        {
            if (_shutdown is not null)
            {
                return Task.FromResult(SupersededResult());
            }

            var revision = _revision;
            return Enqueue(() => Hide(revision, enabling: false));
        }
    }

    public Task<TaskbarOperationResult> RevealAsync()
    {
        lock (_queueGate)
        {
            if (_shutdown is not null)
            {
                return Task.FromResult(SupersededResult());
            }

            var revision = ++_revision;
            return Enqueue(() =>
            {
                if (!IsCurrent(revision))
                {
                    return SupersededResult();
                }

                return IsEnabled
                    ? _controller.ShowAll()
                    : new TaskbarOperationResult(false, 0, "Taskbar replacement is not enabled.");
            });
        }
    }

    public Task<TaskbarOperationResult> DisableAsync()
    {
        lock (_queueGate)
        {
            if (_shutdown is not null)
            {
                return _shutdown;
            }

            ++_revision;
            Volatile.Write(ref _isEnabled, false);
            return Enqueue(_controller.ShowAll);
        }
    }

    // Shutdown invalidates pending hides immediately, then queues the final show
    // behind any in-flight native request. Nothing can enqueue another hide after it.
    public Task<TaskbarOperationResult> ShutdownAsync()
    {
        lock (_queueGate)
        {
            if (_shutdown is not null)
            {
                return _shutdown;
            }

            ++_revision;
            Volatile.Write(ref _isEnabled, false);
            _shutdown = Enqueue(() =>
            {
                try
                {
                    return _controller.ShowAll();
                }
                finally
                {
                    if (_guard is IDisposable disposable)
                    {
                        // Disposing the handle does not terminate the recovery
                        // process: it still restores Explorer if the owner exits.
                        disposable.Dispose();
                    }
                }
            });
            return _shutdown;
        }
    }

    public async ValueTask DisposeAsync() =>
        _ = await ShutdownAsync().ConfigureAwait(false);

    private TaskbarOperationResult Hide(long revision, bool enabling)
    {
        if (!IsCurrent(revision))
        {
            return SupersededResult();
        }

        if (!enabling && !IsEnabled)
        {
            return new TaskbarOperationResult(false, 0, "Taskbar replacement is not enabled.");
        }

        if (!_guard.EnsureStarted(out var guardError))
        {
            Volatile.Write(ref _isEnabled, false);
            return RestoreAfterFailure(new TaskbarOperationResult(
                false, 0, $"The recovery guard could not start. {guardError}"));
        }

        // Guard readiness can take two seconds. A disable/exit received while
        // waiting must not be followed by a fresh hide request.
        if (!IsCurrent(revision))
        {
            return SupersededResult();
        }

        var result = _controller.HideAll();
        lock (_queueGate)
        {
            if (_revision == revision && _shutdown is null)
            {
                Volatile.Write(ref _isEnabled, result.Success);
                if (result.Success)
                {
                    return result;
                }
            }
            else
            {
                result = SupersededResult();
            }
        }

        // A hide already posted to Explorer cannot be withdrawn. Always post
        // the compensating show after it, including when the intent changed.
        return RestoreAfterFailure(result);
    }

    private bool IsCurrent(long revision)
    {
        lock (_queueGate)
        {
            return _revision == revision && _shutdown is null;
        }
    }

    // Caller holds _queueGate. Task.Run also offloads the recovery-guard
    // handshake; no native polling or process readiness wait runs on the UI.
    private Task<TaskbarOperationResult> Enqueue(Func<TaskbarOperationResult> operation)
    {
        var previous = _tail;
        var current = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            try
            {
                return operation();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Volatile.Write(ref _isEnabled, false);
                return RestoreAfterFailure(new TaskbarOperationResult(false, 0, exception.Message));
            }
        });
        _tail = current;
        return current;
    }

    private TaskbarOperationResult RestoreAfterFailure(TaskbarOperationResult result)
    {
        try
        {
            var restore = _controller.ShowAll();
            return restore.Success ? result : result with
            {
                Error = $"{result.Error} Taskbar restoration failed: {restore.Error}",
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return result with { Error = $"{result.Error} Taskbar restoration failed: {exception.Message}" };
        }
    }

    private static TaskbarOperationResult SupersededResult() =>
        new(false, 0, "A newer taskbar request superseded this operation.", Superseded: true);
}
