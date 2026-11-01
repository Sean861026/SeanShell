using System.Runtime.InteropServices;
using System.Text;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class WindowsTaskbarController : ITaskbarController
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpAsyncWindowPos = 0x4000;
    private const int VisibilityPollAttempts = 40;
    private const int VisibilityPollIntervalMilliseconds = 50;
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    private readonly DiagnosticJournal? _diagnostics;

    public WindowsTaskbarController(DiagnosticJournal? diagnostics = null) =>
        _diagnostics = diagnostics;

    public TaskbarOperationResult HideAll() =>
        SetVisibility(visible: false);

    public TaskbarOperationResult ShowAll() =>
        SetVisibility(visible: true);

    private TaskbarOperationResult SetVisibility(bool visible)
    {
        var taskbars = CaptureTaskbars();
        if (taskbars.Count == 0)
        {
            return Failure(
                visible,
                TaskbarFailureReason.NoTaskbars,
                0,
                0,
                "Windows Explorer did not expose a taskbar window.");
        }

        var rejected = 0;
        var flags = SwpAsyncWindowPos | SwpNoMove | SwpNoSize |
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder |
            (visible ? SwpShowWindow : SwpHideWindow);
        foreach (var taskbar in taskbars)
        {
            // Post only visibility to Explorer's thread. Preserve geometry,
            // activation, and both the taskbar's and its owner's Z order.
            if (!SetWindowPos(taskbar, 0, 0, 0, 0, 0, flags))
            {
                rejected++;
            }
        }

        if (rejected > 0)
        {
            return Failure(
                visible,
                TaskbarFailureReason.RequestRejected,
                taskbars.Count,
                rejected,
                $"Windows did not accept the visibility request for {rejected} taskbar window(s).");
        }

        for (var attempt = 0; attempt < VisibilityPollAttempts; attempt++)
        {
            var pending = taskbars
                .Where(taskbar => !IsWindow(taskbar) || IsWindowVisible(taskbar) != visible)
                .ToArray();
            if (pending.Length == 0)
            {
                break;
            }

            // Poll the observed state, not the result of posting the request.
            // Do not flood a hung Explorer thread with duplicate hide messages.
            Thread.Sleep(VisibilityPollIntervalMilliseconds);
        }

        // Success means Windows accepted the request and the state is observed.
        // An async window-position request has no queue-drained acknowledgement.
        // A compensating show can observe visible before an older queued hide runs;
        // the session orders that final show after every hide and keeps the guard.
        var mismatched = taskbars.Count(
            taskbar => !IsWindow(taskbar) || IsWindowVisible(taskbar) != visible);
        return mismatched == 0
            ? new TaskbarOperationResult(true, taskbars.Count)
            : Failure(
                visible,
                TaskbarFailureReason.VisibilityTimedOut,
                taskbars.Count,
                mismatched,
                $"{mismatched} Windows taskbar window(s) did not change visibility.");
    }

    private TaskbarOperationResult Failure(
        bool visible,
        TaskbarFailureReason reason,
        int targetCount,
        int affectedCount,
        string error)
    {
        _diagnostics?.TryWriteTaskbarFailure(
            visible ? TaskbarFailureStage.Show : TaskbarFailureStage.Hide,
            reason,
            targetCount,
            affectedCount);
        return new TaskbarOperationResult(false, targetCount, error);
    }

    private static IReadOnlyList<nint> CaptureTaskbars()
    {
        var taskbars = new HashSet<nint>();
        var primary = FindWindow(PrimaryTaskbarClass, null);
        if (primary != 0)
        {
            taskbars.Add(primary);
        }

        _ = EnumWindows((handle, _) =>
        {
            var className = GetClassName(handle);
            if (string.Equals(
                    className,
                    SecondaryTaskbarClass,
                    StringComparison.Ordinal))
            {
                taskbars.Add(handle);
            }

            return true;
        }, 0);
        return taskbars.ToArray();
    }

    private static string GetClassName(nint handle)
    {
        var className = new StringBuilder(256);
        var length = GetClassName(handle, className, className.Capacity);
        return length > 0 ? className.ToString(0, length) : string.Empty;
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint handle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);
}
