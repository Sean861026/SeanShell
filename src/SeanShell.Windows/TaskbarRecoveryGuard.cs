using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class TaskbarRecoveryGuard : ITaskbarRecoveryGuard, IDisposable
{
    private readonly string _executablePath;
    private readonly int _ownerProcessId;
    private Process? _process;
    private bool _ready;

    public TaskbarRecoveryGuard(string executablePath, int ownerProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (ownerProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerProcessId));
        }

        _executablePath = Path.GetFullPath(executablePath);
        _ownerProcessId = ownerProcessId;
    }

    public bool EnsureStarted(out string? error)
    {
        if (_process is { HasExited: false })
        {
            error = _ready
                ? null
                : "The taskbar recovery guard has not confirmed readiness.";
            return _ready;
        }

        _process?.Dispose();
        _process = null;
        _ready = false;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            startInfo.ArgumentList.Add(TaskbarRecoveryArguments.GuardModeArgument);
            startInfo.ArgumentList.Add(
                _ownerProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            _process = Process.Start(startInfo);
            if (_process is null)
            {
                error = "Windows did not create the taskbar recovery guard.";
                return false;
            }

            var ready = _process.StandardOutput
                .ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(2))
                .GetAwaiter()
                .GetResult();
            if (!string.Equals(
                    ready,
                    TaskbarRecoveryArguments.ReadyMessage,
                    StringComparison.Ordinal))
            {
                error = "The taskbar recovery guard did not confirm readiness.";
                return false;
            }

            _ready = true;
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                TimeoutException or
                System.ComponentModel.Win32Exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
        _ready = false;
    }
}

public static class TaskbarRecoveryEntryPoint
{
    public static bool IsGuardModeRequested(string[] arguments) =>
        TaskbarRecoveryArguments.IsRequested(arguments);

    public static bool TryParseOwnerProcessId(
        string[] arguments,
        out int ownerProcessId)
    {
        return TaskbarRecoveryArguments.TryParseOwnerProcessId(
            arguments,
            out ownerProcessId);
    }

    public static int Run(
        string[] arguments,
        ITaskbarController? controller = null,
        TextWriter? output = null)
    {
        if (!TryParseOwnerProcessId(arguments, out var ownerProcessId))
        {
            return 2;
        }

        controller ??= new WindowsTaskbarController();
        output ??= Console.Out;
        Process? owner = null;
        try
        {
            owner = Process.GetProcessById(ownerProcessId);
            output.WriteLine(TaskbarRecoveryArguments.ReadyMessage);
            output.Flush();
            owner.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The owner exited before the guard acquired its process handle.
        }
        finally
        {
            owner?.Dispose();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (controller.ShowAll().Success)
                {
                    break;
                }

                Thread.Sleep(100);
            }
        }

        return 0;
    }
}
