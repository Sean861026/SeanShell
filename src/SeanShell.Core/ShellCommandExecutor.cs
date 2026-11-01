namespace SeanShell.Core;

public static class ShellCommandExecutor
{
    public static async ValueTask ExecuteAsync(
        ShellCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.ExecutionTimeout is not { } timeout)
        {
            await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "The command deadline must be finite and positive.");
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? operation = null;
        try
        {
            // Invoke on the caller's context: commands can dispatch UI work. A
            // background provider must itself yield before doing blocking work.
            operation = command.ExecuteAsync(cancellation.Token).AsTask();
            await operation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception) when (operation is { IsCompleted: false })
        {
            throw new TimeoutException(
                $"The operation timed out after {timeout.TotalSeconds:0.#} seconds. Please try again.",
                exception);
        }
        finally
        {
            if (operation is { IsCompleted: false })
            {
                // The provider may ignore cancellation. Release the caller now,
                // observe late faults, and keep its token alive until it finishes.
                // Cancellation callbacks must not block the UI thread either.
                var cancelTask = cancellation.CancelAsync();
                _ = Task.WhenAll(operation, cancelTask).ContinueWith(
                    static (completed, state) =>
                    {
                        _ = completed.Exception;
                        ((CancellationTokenSource)state!).Dispose();
                    },
                    cancellation,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else
            {
                cancellation.Dispose();
            }
        }
    }
}
