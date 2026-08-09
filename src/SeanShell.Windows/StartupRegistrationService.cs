using Windows.ApplicationModel;

namespace SeanShell.Windows;

public sealed class StartupRegistrationService
{
    public const string TaskId = "SeanShellStartup";

    public async Task<StartupRegistrationStatus> GetStatusAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return CreateStatus(task.State);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            return StartupRegistrationStatus.Unavailable(exception.Message);
        }
    }

    public async Task<StartupRegistrationStatus> SetEnabledAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            var state = task.State;
            if (enabled)
            {
                if (state == StartupTaskState.Disabled)
                {
                    state = await task.RequestEnableAsync();
                }
            }
            else if (state == StartupTaskState.Enabled)
            {
                task.Disable();
                state = task.State;
            }

            return CreateStatus(state);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            return StartupRegistrationStatus.Unavailable(exception.Message);
        }
    }

    private static StartupRegistrationStatus CreateStatus(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => new(
            state,
            IsEnabled: true,
            CanChange: true,
            "SeanShell starts automatically after you sign in."),
        StartupTaskState.EnabledByPolicy => new(
            state,
            IsEnabled: true,
            CanChange: false,
            "Startup is enabled by your organization."),
        StartupTaskState.DisabledByUser => new(
            state,
            IsEnabled: false,
            CanChange: false,
            "Startup was disabled in Windows. Re-enable SeanShell from Settings > Apps > Startup."),
        StartupTaskState.DisabledByPolicy => new(
            state,
            IsEnabled: false,
            CanChange: false,
            "Startup is disabled by your organization."),
        _ => new(
            state,
            IsEnabled: false,
            CanChange: true,
            "SeanShell starts only when you open it."),
    };
}

public sealed record StartupRegistrationStatus(
    StartupTaskState State,
    bool IsEnabled,
    bool CanChange,
    string Message,
    string? Error = null)
{
    public bool IsAvailable => Error is null;

    internal static StartupRegistrationStatus Unavailable(string error) => new(
        StartupTaskState.DisabledByPolicy,
        IsEnabled: false,
        CanChange: false,
        "Windows startup registration is unavailable for this installation.",
        error);
}
