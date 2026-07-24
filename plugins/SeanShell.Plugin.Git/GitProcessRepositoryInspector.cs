using System.Diagnostics;

namespace SeanShell.Plugin.Git;

public sealed class GitProcessRepositoryInspector : IGitRepositoryInspector
{
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

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            return process.ExitCode == 0
                ? GitStatusParser.Parse(repositoryPath, output)
                : null;
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
        catch
        {
            TryTerminate(process);
            return null;
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
            // The process may have already exited or become inaccessible.
        }
    }
}
