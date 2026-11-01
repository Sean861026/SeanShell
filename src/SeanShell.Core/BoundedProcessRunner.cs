using System.Diagnostics;

namespace SeanShell.Core;

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

/// <summary>
/// Runs an owned, redirected query process with a deadline that also covers pipe draining.
/// This must not be used for user-launched terminals or other intentionally long-lived apps.
/// </summary>
public static class BoundedProcessRunner
{
    public static Task<ProcessExecutionResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        RunAsync(startInfo, timeout, cancellationToken, processStarted: null);

    internal static Task<ProcessExecutionResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<Process>? processStarted)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite positive deadline is required.");
        }

        if (startInfo.UseShellExecute || !startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError)
        {
            throw new ArgumentException("Query processes must redirect stdout and stderr without shell execution.", nameof(startInfo));
        }

        return RunWithDeadlineAsync(startInfo, timeout, cancellationToken, processStarted);
    }

    private static async Task<ProcessExecutionResult> RunWithDeadlineAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<Process>? processStarted)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = new CancellationTokenSource(timeout);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var operationToken = lifetime.Token;
        // Executable discovery, Start, or native process cleanup can block synchronously.
        // The worker owns cleanup; the caller's wait remains bounded even during those calls.
        var worker = Task.Run(() => RunCoreAsync(startInfo, operationToken, processStarted), CancellationToken.None);
        try
        {
            return await worker.WaitAsync(operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (operationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The query process was canceled.", exception, cancellationToken);
            }

            throw new TimeoutException(
                $"'{Path.GetFileName(startInfo.FileName)}' did not finish within {timeout.TotalSeconds:0.###} seconds.",
                exception);
        }
        finally
        {
            // Keep the token sources alive until the worker finishes, even when Start
            // returns after the caller timed out. It must still see cancellation and kill
            // any process it just created. Also observe late worker/cleanup failures.
            _ = worker.ContinueWith(completed =>
            {
                _ = completed.Exception;
                lifetime.Dispose();
                deadline.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static async Task<ProcessExecutionResult> RunCoreAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken,
        Action<Process>? processStarted)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var process = new Process { StartInfo = startInfo };
        Task<string>? outputTask = null;
        Task<string>? errorTask = null;
        Task? exitTask = null;
        Task? completion = null;
        var started = false;
        var succeeded = false;

        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException($"Unable to start query process '{startInfo.FileName}'.");
            }

            // Internal test seam: capture ownership before cleanup can race the observer,
            // independently of how long the child interpreter takes to initialize.
            processStarted?.Invoke(process);
            lifetime.Token.ThrowIfCancellationRequested();
            outputTask = process.StandardOutput.ReadToEndAsync(lifetime.Token);
            errorTask = process.StandardError.ReadToEndAsync(lifetime.Token);
            exitTask = process.WaitForExitAsync(lifetime.Token);
            completion = Task.WhenAll(exitTask, outputTask, errorTask);

            // An exited parent can leave inherited pipe handles open in a child. Bound the
            // complete operation, not only WaitForExitAsync or just one of the two streams.
            await completion.WaitAsync(lifetime.Token).ConfigureAwait(false);
            var result = new ProcessExecutionResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
            succeeded = true;
            return result;
        }
        finally
        {
            if (!succeeded)
            {
                lifetime.Cancel();
                if (started)
                {
                    TryTerminate(process);
                    TryClose(process.StandardOutput);
                    TryClose(process.StandardError);
                }
            }

            // Cancellation/failure may win before another reader finishes. Observe every
            // task (including WhenAll) without replacing the original error or waiting
            // indefinitely for a broken process/pipe to acknowledge cancellation.
            ObserveFailure(outputTask);
            ObserveFailure(errorTask);
            ObserveFailure(exitTask);
            ObserveFailure(completion);
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // A descendant may have disappeared or be inaccessible. Still try to terminate
            // the exact process we started; never look up or kill unrelated processes.
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Preserve the operation's cancellation/failure if termination is denied.
            }
        }
    }

    private static void TryClose(IDisposable stream)
    {
        try
        {
            stream.Dispose();
        }
        catch
        {
            // A canceled read can already have closed its pipe.
        }
    }

    private static void ObserveFailure(Task? task)
    {
        if (task is not null)
        {
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
