using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Plugin.Git;

public sealed class GitProcessRepositoryInspector : IGitRepositoryInspector
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> _runAsync;

    public GitProcessRepositoryInspector() : this(BoundedProcessRunner.RunAsync)
    {
    }

    internal GitProcessRepositoryInspector(
        Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    public async ValueTask<GitRepositorySnapshot?> InspectAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repositoryPath);
        startInfo.ArgumentList.Add("status");
        startInfo.ArgumentList.Add("--porcelain=v1");
        startInfo.ArgumentList.Add("--branch");

        try
        {
            var result = await _runAsync(startInfo, QueryTimeout, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0
                ? GitStatusParser.Parse(repositoryPath, result.StandardOutput)
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
