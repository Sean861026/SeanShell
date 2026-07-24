namespace SeanShell.Plugin.Docker;

public sealed record DockerContainerQueryResult(
    bool IsAvailable,
    string StatusText,
    IReadOnlyList<DockerContainerSnapshot> Containers)
{
    public static DockerContainerQueryResult CliUnavailable { get; } =
        new(false, "Docker CLI unavailable", []);

    public static DockerContainerQueryResult EngineUnavailable { get; } =
        new(false, "Docker Engine unavailable", []);
}
