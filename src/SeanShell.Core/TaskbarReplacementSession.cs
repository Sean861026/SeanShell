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
    string? Error = null);

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

public sealed class TaskbarReplacementSession : IDisposable
{
    private readonly ITaskbarController _controller;
    private readonly ITaskbarRecoveryGuard _guard;

    public TaskbarReplacementSession(
        ITaskbarController controller,
        ITaskbarRecoveryGuard guard)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(guard);
        _controller = controller;
        _guard = guard;
    }

    public bool IsEnabled { get; private set; }

    public TaskbarOperationResult Enable()
    {
        if (!_guard.EnsureStarted(out var guardError))
        {
            IsEnabled = false;
            _ = _controller.ShowAll();
            return new TaskbarOperationResult(
                false,
                0,
                $"The recovery guard could not start. {guardError}");
        }

        var result = _controller.HideAll();
        IsEnabled = result.Success;
        if (!result.Success)
        {
            _ = _controller.ShowAll();
        }

        return result;
    }

    public TaskbarOperationResult EnsureHidden()
    {
        if (!IsEnabled)
        {
            return new TaskbarOperationResult(
                false,
                0,
                "Taskbar replacement is not enabled.");
        }

        if (!_guard.EnsureStarted(out var guardError))
        {
            IsEnabled = false;
            _ = _controller.ShowAll();
            return new TaskbarOperationResult(
                false,
                0,
                $"The recovery guard stopped and could not restart. {guardError}");
        }

        return _controller.HideAll();
    }

    public TaskbarOperationResult Disable()
    {
        IsEnabled = false;
        return _controller.ShowAll();
    }

    public void Dispose()
    {
        _ = Disable();
        if (_guard is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
