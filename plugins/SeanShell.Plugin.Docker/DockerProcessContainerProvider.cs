using System.ComponentModel;
using System.Diagnostics;
using SeanShell.Core;

namespace SeanShell.Plugin.Docker;

public sealed class DockerProcessContainerProvider : IDockerContainerProvider
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> _runAsync;

    public DockerProcessContainerProvider() : this(BoundedProcessRunner.RunAsync)
    {
    }

    internal DockerProcessContainerProvider(
        Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    public async ValueTask<DockerContainerQueryResult> GetContainersAsync(
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("container");
        startInfo.ArgumentList.Add("ls");
        startInfo.ArgumentList.Add("--all");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{json .}}");

        try
        {
            var result = await _runAsync(startInfo, QueryTimeout, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return DockerContainerQueryResult.EngineUnavailable;
            }

            var containers = DockerContainerParser.Parse(result.StandardOutput);
            var running = containers.Count(static container =>
                string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase));
            return new(
                true,
                $"{containers.Count} container{(containers.Count == 1 ? string.Empty : "s")} · {running} running",
                containers);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Win32Exception)
        {
            return DockerContainerQueryResult.CliUnavailable;
        }
        catch
        {
            return DockerContainerQueryResult.EngineUnavailable;
        }
    }
}
