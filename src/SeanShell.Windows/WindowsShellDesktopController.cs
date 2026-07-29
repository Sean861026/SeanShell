using System.Reflection;
using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class WindowsShellDesktopController : IDesktopVisibilityController
{
    public DesktopVisibilityResult MinimizeAll() =>
        InvokeShellMethod("MinimizeAll", "show the desktop");

    public DesktopVisibilityResult UndoMinimizeAll() =>
        InvokeShellMethod("UndoMinimizeAll", "restore minimized windows");

    private static DesktopVisibilityResult InvokeShellMethod(
        string methodName,
        string operation)
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return new DesktopVisibilityResult(
                    false,
                    "Windows Shell automation is unavailable.");
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return new DesktopVisibilityResult(
                    false,
                    "Windows Shell automation could not be started.");
            }

            shellType.InvokeMember(
                methodName,
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: null);
            return new DesktopVisibilityResult(true);
        }
        catch (Exception exception)
        {
            var source = exception is TargetInvocationException
            {
                InnerException: not null,
            }
                ? exception.InnerException
                : exception;
            return new DesktopVisibilityResult(
                false,
                $"Windows could not {operation}. {source.Message}");
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
