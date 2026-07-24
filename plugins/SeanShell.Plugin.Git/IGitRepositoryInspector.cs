namespace SeanShell.Plugin.Git;

public interface IGitRepositoryInspector
{
    ValueTask<GitRepositorySnapshot?> InspectAsync(
        string repositoryPath,
        CancellationToken cancellationToken);
}
