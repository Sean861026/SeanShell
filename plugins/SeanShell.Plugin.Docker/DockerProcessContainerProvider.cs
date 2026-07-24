using System.ComponentModel;
using System.Diagnostics;

namespace SeanShell.Plugin.Docker;

public sealed class DockerProcessContainerProvider : IDockerContainerProvider
{
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

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return DockerContainerQueryResult.CliUnavailable;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return DockerContainerQueryResult.EngineUnavailable;
            }

            var containers = DockerContainerParser.Parse(output);
            var running = containers.Count(static container =>
                string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase));
            return new(
                true,
                $"{containers.Count} container{(containers.Count == 1 ? string.Empty : "s")} · {running} running",
                containers);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
        catch (Win32Exception)
        {
            TryTerminate(process);
            return DockerContainerQueryResult.CliUnavailable;
        }
        catch
        {
            TryTerminate(process);
            return DockerContainerQueryResult.EngineUnavailable;
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
