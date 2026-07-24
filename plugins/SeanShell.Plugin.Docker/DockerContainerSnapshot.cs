namespace SeanShell.Plugin.Docker;

public sealed record DockerContainerSnapshot(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    IReadOnlyList<DockerPublishedPort> PublishedPorts)
{
    public string StatusText =>
        $"{NormalizeState(State)} · {Image} · {Status}";

    private static string NormalizeState(string state) =>
        string.IsNullOrWhiteSpace(state)
            ? "Unknown"
            : char.ToUpperInvariant(state[0]) + state[1..].ToLowerInvariant();
}
