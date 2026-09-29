namespace SeanShell.Core;

public readonly record struct LauncherOperation(long Session, long Id);

/// <summary>
/// UI-thread-owned Launcher lifetime and single-flight action gate. Hiding a
/// window invalidates its UI continuations, but does not undo an action already
/// dispatched to an application or settings provider.
/// </summary>
public sealed class LauncherOperationLifetime
{
    private long _session;
    private long _operationId;
    private LauncherOperation? _activeOperation;

    public bool IsVisible { get; private set; }

    public bool IsShutdown { get; private set; }

    public bool IsBusy => _activeOperation is not null;

    public long Session => _session;

    public bool TryShow(out long session)
    {
        session = _session;
        if (IsShutdown)
        {
            return false;
        }

        session = ++_session;
        IsVisible = true;
        return true;
    }

    public void Hide() => IsVisible = false;

    public void Shutdown()
    {
        IsShutdown = true;
        IsVisible = false;
    }

    public bool IsCurrent(long session) =>
        !IsShutdown && IsVisible && session == _session;

    public bool TryBegin(out LauncherOperation operation)
    {
        operation = default;
        if (!IsVisible || IsShutdown || IsBusy)
        {
            return false;
        }

        operation = new LauncherOperation(_session, ++_operationId);
        _activeOperation = operation;
        return true;
    }

    public bool CanApply(LauncherOperation operation) =>
        _activeOperation == operation && IsCurrent(operation.Session);

    public bool Complete(LauncherOperation operation)
    {
        if (_activeOperation != operation)
        {
            return false;
        }

        _activeOperation = null;
        return true;
    }
}
