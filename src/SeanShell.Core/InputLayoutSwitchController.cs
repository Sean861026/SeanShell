namespace SeanShell.Core;

public sealed record InputWindowIdentity(nint Handle, uint ThreadId, uint ProcessId);

public sealed record InputLayoutOption(nint Handle, string DisplayName);

public sealed record InputLayoutSnapshot(
    InputWindowIdentity? Target,
    nint ActiveLayout,
    IReadOnlyList<InputLayoutOption> Layouts);

public enum InputLayoutSwitchResult
{
    NoTarget,
    TargetChanged,
    FocusNotRestored,
    LayoutUnavailable,
    AlreadyActive,
    RequestPosted,
    RequestFailed,
    Confirmed,
    NotConfirmed,
}

public interface IInputLayoutBackend
{
    InputWindowIdentity? CaptureTarget(nint handle);
    IReadOnlyList<InputLayoutOption> CaptureLayouts();
    nint ReadActiveLayout(InputWindowIdentity target);
    bool IsForeground(InputWindowIdentity target);
    bool TryPostChange(InputWindowIdentity target, nint layout);
}

/// <summary>
/// Requests an explicit change only for the remembered external application's
/// current foreground thread. A queued message is never reported as confirmation.
/// </summary>
public sealed class InputLayoutSwitchController(IInputLayoutBackend backend, uint shellProcessId)
{
    public InputLayoutSnapshot Capture(nint handle)
    {
        var target = backend.CaptureTarget(handle);
        if (!IsExternalTarget(target))
        {
            target = null;
        }

        var layouts = backend.CaptureLayouts()
            .Where(static layout => layout.Handle != 0)
            .DistinctBy(static layout => layout.Handle)
            .Take(64)
            .ToArray();
        return new InputLayoutSnapshot(
            target, target is null ? 0 : backend.ReadActiveLayout(target), layouts);
    }

    public InputLayoutSwitchResult Request(InputWindowIdentity? target, nint layout)
    {
        if (!IsExternalTarget(target))
        {
            return InputLayoutSwitchResult.NoTarget;
        }

        if (backend.CaptureTarget(target!.Handle) != target)
        {
            return InputLayoutSwitchResult.TargetChanged;
        }

        if (!backend.IsForeground(target))
        {
            return InputLayoutSwitchResult.FocusNotRestored;
        }

        if (layout == 0 || !backend.CaptureLayouts().Any(option => option.Handle == layout))
        {
            return InputLayoutSwitchResult.LayoutUnavailable;
        }

        if (backend.ReadActiveLayout(target) == layout)
        {
            return InputLayoutSwitchResult.AlreadyActive;
        }

        return backend.TryPostChange(target, layout)
            ? InputLayoutSwitchResult.RequestPosted
            : InputLayoutSwitchResult.RequestFailed;
    }

    public bool IsCurrentTarget(InputWindowIdentity? target) =>
        IsExternalTarget(target) && backend.CaptureTarget(target!.Handle) == target;

    public InputLayoutSwitchResult Confirm(InputWindowIdentity target, nint layout)
    {
        if (!IsExternalTarget(target) || backend.CaptureTarget(target.Handle) != target)
        {
            return InputLayoutSwitchResult.TargetChanged;
        }

        return layout != 0 && backend.ReadActiveLayout(target) == layout
            ? InputLayoutSwitchResult.Confirmed
            : InputLayoutSwitchResult.NotConfirmed;
    }

    private bool IsExternalTarget(InputWindowIdentity? target) =>
        target is { Handle: not 0, ThreadId: not 0, ProcessId: not 0 } &&
        target.ProcessId != shellProcessId;
}
