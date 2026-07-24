using SeanShell.Plugin.Git;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class GitPluginTests
{
    [TestMethod]
    public void StatusParserReadsBranchChangesAndTrackingState()
    {
        var path = Path.Combine(Path.GetTempPath(), "SeanShell-parser-repository");

        var snapshot = GitStatusParser.Parse(
            path,
            "## main...origin/main [ahead 2, behind 1]\n M README.md\n?? note.txt\n");

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("main", snapshot.Branch);
        Assert.AreEqual(2, snapshot.ChangedFileCount);
        Assert.AreEqual("ahead 2, behind 1", snapshot.TrackingStatus);
        Assert.AreEqual("main \u00B7 2 changes \u00B7 ahead 2, behind 1", snapshot.StatusText);
    }

    [TestMethod]
    public void StatusParserHandlesNewAndDetachedRepositories()
    {
        var path = Path.Combine(Path.GetTempPath(), "SeanShell-parser-repository");

        var newRepository = GitStatusParser.Parse(path, "## No commits yet on trunk\n");
        var detachedRepository = GitStatusParser.Parse(path, "## HEAD (no branch)\n");

        Assert.AreEqual("trunk", newRepository?.Branch);
        Assert.AreEqual("detached HEAD", detachedRepository?.Branch);
    }

    [TestMethod]
    public void DiscoveryFindsRepositoriesWithinBoundedDepth()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "first"));
            Directory.CreateDirectory(Path.Combine(first.FullName, ".git"));
            var second = Directory.CreateDirectory(Path.Combine(root, "group", "second"));
            Directory.CreateDirectory(Path.Combine(second.FullName, ".git"));
            var tooDeep = Directory.CreateDirectory(
                Path.Combine(root, "group", "deeper", "third"));
            Directory.CreateDirectory(Path.Combine(tooDeep.FullName, ".git"));

            var repositories = GitRepositoryDiscovery.Discover(
                [root],
                maximumDepth: 2);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    Path.GetFullPath(first.FullName),
                    Path.GetFullPath(second.FullName),
                },
                repositories.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DiscoveryFindsRepositoryContainingADeepApplicationDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var applicationDirectory = Directory.CreateDirectory(
                Path.Combine(root, "src", "App", "bin", "Release", "AppX"));

            var containingRepository = GitRepositoryDiscovery.FindContainingRepository(
                applicationDirectory.FullName);
            var repositories = GitRepositoryDiscovery.Discover(
                [applicationDirectory.FullName]);

            Assert.AreEqual(Path.GetFullPath(root), containingRepository);
            CollectionAssert.AreEqual(
                new[] { Path.GetFullPath(root) },
                repositories.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task PluginReturnsCachedRepositoryCommandsAndRefreshesOnRequest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var inspector = new StubInspector();
            await using var plugin = new GitPlugin([root], inspector);

            await plugin.InitializeAsync(CancellationToken.None);
            var initialCommands = await plugin.GetCommandsAsync(
                string.Empty,
                CancellationToken.None);

            Assert.HasCount(4, initialCommands);
            Assert.AreEqual(
                "main \u00B7 clean \u00B7 Open repository folder",
                initialCommands.Single(command => command.Title == Path.GetFileName(root)).Subtitle);
            Assert.IsTrue(initialCommands.Any(command => command.Title.EndsWith("in VS Code")));
            Assert.IsTrue(initialCommands.Any(command => command.Title.EndsWith("terminal")));

            var refresh = initialCommands.Single(
                command => command.Title == "Refresh Git repositories");
            await refresh.ExecuteAsync(CancellationToken.None);
            var refreshedCommands = await plugin.GetCommandsAsync(
                string.Empty,
                CancellationToken.None);

            Assert.AreEqual(
                "feature/plugin \u00B7 1 change \u00B7 Open repository folder",
                refreshedCommands.Single(
                    command => command.Title == Path.GetFileName(root)).Subtitle);
            Assert.AreEqual(2, inspector.CallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SeanShell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubInspector : IGitRepositoryInspector
    {
        public int CallCount { get; private set; }

        public ValueTask<GitRepositorySnapshot?> InspectAsync(
            string repositoryPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<GitRepositorySnapshot?>(
                new(
                    repositoryPath,
                    Path.GetFileName(repositoryPath),
                    CallCount == 1 ? "main" : "feature/plugin",
                    CallCount == 1 ? 0 : 1));
        }
    }
}
