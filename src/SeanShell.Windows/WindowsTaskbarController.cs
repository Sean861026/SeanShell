using System.Runtime.InteropServices;
using System.Text;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class WindowsTaskbarController : ITaskbarController
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int VisibilityPollAttempts = 40;
    private const int VisibilityPollIntervalMilliseconds = 50;
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

    public TaskbarOperationResult HideAll() =>
        SetVisibility(visible: false);

    public TaskbarOperationResult ShowAll() =>
        SetVisibility(visible: true);

    private static TaskbarOperationResult SetVisibility(bool visible)
    {
        var taskbars = CaptureTaskbars();
        if (taskbars.Count == 0)
        {
            return new TaskbarOperationResult(
                false,
                0,
                "Windows Explorer did not expose a taskbar window.");
        }

        var rejected = 0;
        foreach (var taskbar in taskbars)
        {
            if (!ShowWindowAsync(taskbar, visible ? SwShow : SwHide))
            {
                rejected++;
            }
        }

        if (rejected > 0)
        {
            return new TaskbarOperationResult(
                false,
                taskbars.Count,
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
        // ShowWindowAsync exposes no queue-drained acknowledgement. In particular,
        // a compensating show can observe visible before an older queued hide runs;
        // the session orders that final show after every hide and keeps the guard.
        var mismatched = taskbars.Count(
            taskbar => !IsWindow(taskbar) || IsWindowVisible(taskbar) != visible);
        return mismatched == 0
            ? new TaskbarOperationResult(true, taskbars.Count)
            : new TaskbarOperationResult(
                false,
                taskbars.Count,
                $"{mismatched} Windows taskbar window(s) did not change visibility.");
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);
}
