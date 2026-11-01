namespace SeanShell.Core;

/// <summary>Isolates optional presentation cleanup from the shell's recovery path.</summary>
public static class ActivityIslandCleanup
{
    public static bool TryRun(Action action, Action<Exception>? reportFailure = null)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                reportFailure?.Invoke(exception);
            }
            catch (Exception reportingFailure) when (reportingFailure is not OutOfMemoryException)
            {
                // Diagnostic failure must not prevent the next cleanup step.
            }

            return false;
        }
    }
}
