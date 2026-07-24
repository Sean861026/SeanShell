namespace SeanShell.Plugin.Docker;

public sealed record DockerPublishedPort(int HostPort, int ContainerPort);
