using System.Runtime.InteropServices;
using System.Text;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class WindowsTaskbarController : ITaskbarController
{
    private const int SwHide = 0;
    private const int SwShow = 5;
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

        foreach (var taskbar in taskbars)
        {
            _ = ShowWindow(taskbar, visible ? SwShow : SwHide);
        }

        var mismatched = taskbars.Count(
            taskbar => IsWindowVisible(taskbar) != visible);
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
    private static extern bool ShowWindow(nint handle, int command);
}
