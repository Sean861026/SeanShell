using SeanShell.Core;
using SeanShell.PluginContracts;
using SeanShell.Plugins;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PluginHostTests
{
    [TestMethod]
    public async Task InitializeAndQueryExposeHealthyPluginCommands()
    {
        var plugin = new TestPlugin("test.healthy", "Healthy plugin")
        {
            Commands = [CreateCommand("healthy")],
        };
        await using var host = CreateHost(plugin);

        await host.InitializeAsync();
        var commands = await host.GetCommandsAsync("healthy", CancellationToken.None);

        Assert.HasCount(1, commands);
        Assert.AreEqual("healthy", commands[0].Id);
        Assert.AreEqual(PluginRuntimeState.Active, host.Diagnostics.Single().State);
    }

    [TestMethod]
    public async Task QueryTimeoutFaultsOnlyTheUnresponsivePlugin()
    {
        var stalled = new TestPlugin("test.stalled", "Stalled plugin")
        {
            QueryAsync = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            },
        };
        var healthy = new TestPlugin("test.healthy", "Healthy plugin")
        {
            Commands = [CreateCommand("healthy")],
        };
        await using var host = CreateHost(stalled, healthy);
        await host.InitializeAsync();

        var commands = await host.GetCommandsAsync("healthy", CancellationToken.None);

        Assert.HasCount(1, commands);
        Assert.AreEqual("healthy", commands[0].Id);
        Assert.AreEqual(
            PluginRuntimeState.Faulted,
            host.Diagnostics.Single(diagnostic => diagnostic.Id == stalled.Id).State);
        StringAssert.Contains(
            host.Diagnostics.Single(diagnostic => diagnostic.Id == stalled.Id).LastError,
            "limit");
        Assert.AreEqual(
            PluginRuntimeState.Active,
            host.Diagnostics.Single(diagnostic => diagnostic.Id == healthy.Id).State);
    }

    [TestMethod]
    public async Task SuspendAndResumeAreIdempotentStateTransitions()
    {
        var plugin = new TestPlugin("test.lifecycle", "Lifecycle plugin");
        await using var host = CreateHost(plugin);
        await host.InitializeAsync();

        await host.SuspendAsync();
        await host.SuspendAsync();
        Assert.AreEqual(1, plugin.SuspendCount);
        Assert.AreEqual(PluginRuntimeState.Suspended, host.Diagnostics.Single().State);

        await host.ResumeAsync();
        await host.ResumeAsync();
        Assert.AreEqual(1, plugin.ResumeCount);
        Assert.AreEqual(PluginRuntimeState.Active, host.Diagnostics.Single().State);
    }

    [TestMethod]
    public async Task FinishingQueryDoesNotOverwriteConcurrentSuspendState()
    {
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishQuery = new TaskCompletionSource<IReadOnlyList<ShellCommand>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new TestPlugin("test.concurrent", "Concurrent plugin")
        {
            QueryAsync = _ =>
            {
                queryStarted.SetResult();
                return finishQuery.Task;
            },
        };
        await using var host = new PluginHost(
            [new PluginRegistration(CreateManifest(plugin), plugin)],
            new PluginHostOptions(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100)));
        await host.InitializeAsync();

        var query = host.GetCommandsAsync(string.Empty, CancellationToken.None).AsTask();
        await queryStarted.Task;
        await host.SuspendAsync();
        finishQuery.SetResult([]);
        await query;

        Assert.AreEqual(PluginRuntimeState.Suspended, host.Diagnostics.Single().State);
    }

    [TestMethod]
    public void ConstructorRejectsThirdPartyRegistrationBeforeIsolationExists()
    {
        var plugin = new TestPlugin("test.external", "External plugin");
        var manifest = CreateManifest(plugin) with { IsBuiltIn = false };

        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            new PluginHost([new PluginRegistration(manifest, plugin)]));

        StringAssert.Contains(exception.Message, "Third-party plugin loading is disabled");
    }

    private static PluginHost CreateHost(params TestPlugin[] plugins) =>
        new(
            plugins.Select(plugin => new PluginRegistration(CreateManifest(plugin), plugin)),
            new PluginHostOptions(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(100)));

    private static PluginManifest CreateManifest(TestPlugin plugin) =>
        new(
            PluginManifest.CurrentSchemaVersion,
            plugin.Id,
            plugin.Name,
            "1.0.0",
            PluginHost.HostApiVersion,
            "SeanShell Tests",
            PluginCapability.LauncherCommands,
            true);

    private static ShellCommand CreateCommand(string id) =>
        new(id, id, null, _ => ValueTask.CompletedTask);

    private sealed class TestPlugin(string id, string name) : ISeanShellPlugin
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public IReadOnlyList<ShellCommand> Commands { get; init; } = [];

        public Func<CancellationToken, Task<IReadOnlyList<ShellCommand>>>? QueryAsync { get; init; }

        public int SuspendCount { get; private set; }

        public int ResumeCount { get; private set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public async ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
            string query,
            CancellationToken cancellationToken)
        {
            if (QueryAsync is not null)
            {
                return await QueryAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Commands;
        }

        public ValueTask SuspendAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SuspendCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
