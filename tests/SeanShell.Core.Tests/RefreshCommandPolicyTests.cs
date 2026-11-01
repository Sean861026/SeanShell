using SeanShell.Plugin.Docker;
using SeanShell.Plugin.DotNet;
using SeanShell.Plugin.Git;
using SeanShell.Plugin.Wsl;
using SeanShell.PluginContracts;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class RefreshCommandPolicyTests
{
    [TestMethod]
    public async Task BuiltInRefreshCommandsHaveFiniteDeadline()
    {
        ISeanShellPlugin[] plugins = [new GitPlugin([]), new WslPlugin(), new DockerPlugin(), new DotNetWorkspacePlugin([])];
        foreach (var plugin in plugins)
        {
            await using var lifetime = plugin;
            var commands = await plugin.GetCommandsAsync(string.Empty, CancellationToken.None);
            var refresh = commands.Single(command => command.Id.EndsWith(":refresh", StringComparison.Ordinal));
            Assert.AreEqual(TimeSpan.FromSeconds(30), refresh.ExecutionTimeout, plugin.Name);
            Assert.IsTrue(commands.Where(command => command != refresh).All(command => command.ExecutionTimeout is null));
        }
    }

    [TestMethod]
    public async Task CanceledWslRefreshDoesNotPublishLateProviderResult()
    {
        var provider = new LateWslProvider();
        await using var plugin = new WslPlugin(provider);
        await plugin.InitializeAsync(CancellationToken.None);
        var commands = await plugin.GetCommandsAsync(string.Empty, CancellationToken.None);
        var refresh = commands.Single(command => command.Id.EndsWith(":refresh", StringComparison.Ordinal));
        using var cancellation = new CancellationTokenSource();
        var operation = refresh.ExecuteAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        provider.Result.SetResult([new("Late distribution", "Running", 2, false)]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        var after = await plugin.GetCommandsAsync(string.Empty, CancellationToken.None);
        CollectionAssert.AreEqual(commands.Select(command => command.Title).ToArray(), after.Select(command => command.Title).ToArray());
    }

    [TestMethod]
    public async Task CanceledDockerRefreshDoesNotReplacePreviousAvailability()
    {
        var provider = new LateDockerProvider();
        await using var plugin = new DockerPlugin(provider);
        await plugin.InitializeAsync(CancellationToken.None);
        var commands = await plugin.GetCommandsAsync(string.Empty, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var operation = commands[0].ExecuteAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        provider.Result.SetResult(new DockerContainerQueryResult(true, "Late result", []));

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        var after = await plugin.GetCommandsAsync(string.Empty, CancellationToken.None);
        Assert.AreEqual(commands[0].Subtitle, after[0].Subtitle);
    }

    private sealed class LateWslProvider : IWslDistributionProvider
    {
        private bool _initialized;
        public TaskCompletionSource<IReadOnlyList<WslDistributionSnapshot>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IReadOnlyList<WslDistributionSnapshot>> GetDistributionsAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
            {
                return new(Result.Task);
            }

            _initialized = true;
            return ValueTask.FromResult<IReadOnlyList<WslDistributionSnapshot>>([new("Initial", "Stopped", 2, true)]);
        }
    }

    private sealed class LateDockerProvider : IDockerContainerProvider
    {
        private bool _initialized;
        public TaskCompletionSource<DockerContainerQueryResult> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<DockerContainerQueryResult> GetContainersAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
            {
                return new(Result.Task);
            }

            _initialized = true;
            return ValueTask.FromResult(DockerContainerQueryResult.EngineUnavailable);
        }
    }
}
