namespace SeanShell.Plugin.Docker;

public interface IDockerContainerProvider
{
    ValueTask<DockerContainerQueryResult> GetContainersAsync(
        CancellationToken cancellationToken);
}
