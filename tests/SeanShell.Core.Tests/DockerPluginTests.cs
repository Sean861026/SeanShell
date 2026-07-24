using SeanShell.Plugin.Docker;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DockerPluginTests
{
    [TestMethod]
    public void ParserReadsContainersAndDeduplicatesPublishedTcpPorts()
    {
        const string output =
            """
            {"ID":"abc123","Image":"nginx:latest","Names":"web","Ports":"0.0.0.0:8080->80/tcp, [::]:8080->80/tcp, 53/udp","State":"running","Status":"Up 2 minutes"}
            {"ID":"def456","Image":"postgres:17","Names":"db","Ports":"127.0.0.1:5432->5432/tcp","State":"exited","Status":"Exited (0) 1 hour ago"}
            not-json
            """;

        var containers = DockerContainerParser.Parse(output);

        Assert.HasCount(2, containers);
        Assert.AreEqual("db", containers[0].Name);
        Assert.AreEqual("Exited · postgres:17 · Exited (0) 1 hour ago", containers[0].StatusText);
        Assert.HasCount(1, containers[0].PublishedPorts);
        Assert.AreEqual(new DockerPublishedPort(5432, 5432), containers[0].PublishedPorts[0]);
        Assert.AreEqual("web", containers[1].Name);
        Assert.HasCount(1, containers[1].PublishedPorts);
        Assert.AreEqual(new DockerPublishedPort(8080, 80), containers[1].PublishedPorts[0]);
    }

    [TestMethod]
    public void LogsStartInfoUsesStructuredArgumentsAndUserProfile()
    {
        var startInfo = DockerCommandStartInfoFactory.CreateLogs("abc 123");

        Assert.AreEqual(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            startInfo.WorkingDirectory);
        Assert.IsTrue(startInfo.UseShellExecute);
        CollectionAssert.AreEqual(
            new[] { "logs", "--tail", "200", "--follow", "abc 123" },
            startInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    public async Task OfflineEngineRemainsHealthyAndExplicitRefreshReplacesCache()
    {
        var provider = new StubProvider();
        await using var plugin = new DockerPlugin(provider);

        await plugin.InitializeAsync(CancellationToken.None);
        var offlineCommands = await plugin.GetCommandsAsync(
            string.Empty,
            CancellationToken.None);

        Assert.HasCount(1, offlineCommands);
        Assert.AreEqual(
            "Docker Engine unavailable",
            offlineCommands[0].Subtitle);

        await offlineCommands[0].ExecuteAsync(CancellationToken.None);
        var onlineCommands = await plugin.GetCommandsAsync(
            string.Empty,
            CancellationToken.None);

        Assert.HasCount(4, onlineCommands);
        Assert.IsTrue(onlineCommands.Any(command =>
            command.Title == "api Docker logs"));
        Assert.IsTrue(onlineCommands.Any(command =>
            command.Title == "api Docker port 5000"));
        Assert.IsTrue(onlineCommands.Any(command =>
            command.Title == "db Docker logs"));
        Assert.IsFalse(onlineCommands.Any(command =>
            command.Title == "db Docker port 5432"));
        Assert.AreEqual(2, provider.CallCount);
    }

    private sealed class StubProvider : IDockerContainerProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<DockerContainerQueryResult> GetContainersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(
                CallCount == 1
                    ? DockerContainerQueryResult.EngineUnavailable
                    : new DockerContainerQueryResult(
                        true,
                        "2 containers · 1 running",
                        [
                            new(
                                "abc123",
                                "api",
                                "sample/api:latest",
                                "running",
                                "Up 10 seconds",
                                [new(5000, 8080)]),
                            new(
                                "def456",
                                "db",
                                "postgres:17",
                                "exited",
                                "Exited (0) 1 minute ago",
                                [new(5432, 5432)]),
                        ]));
        }
    }
}
