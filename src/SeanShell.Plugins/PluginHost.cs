using System.Diagnostics;
using SeanShell.Core;
using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed class PluginHost : ILauncherCommandProvider, IAsyncDisposable
{
    public const int HostApiVersion = 1;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly PluginHostOptions _options;
    private readonly PluginRuntime[] _plugins;
    private bool _disposed;
    private bool _initialized;

    public PluginHost(
        IEnumerable<PluginRegistration> registrations,
        PluginHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _options = options ?? PluginHostOptions.Default;
        _options.Validate();
        _plugins = registrations.Select(CreateRuntime).ToArray();

        var duplicate = _plugins
            .GroupBy(static plugin => plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Plugin ID '{duplicate.Key}' is registered more than once.", nameof(registrations));
        }
    }

    public event EventHandler? DiagnosticsChanged;

    public IReadOnlyList<PluginDiagnostic> Diagnostics =>
        _plugins.Select(static plugin => plugin.CreateDiagnostic()).ToArray();

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            foreach (var plugin in _plugins)
            {
                await RunLifecycleAsync(
                    plugin,
                    "Initialize",
                    _options.InitializationTimeout,
                    plugin.Instance.InitializeAsync,
                    PluginRuntimeState.Active,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ShellCommand>> GetCommandsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var queryTasks = _plugins
            .Where(static plugin =>
                plugin.State == PluginRuntimeState.Active &&
                plugin.Manifest.Capabilities.HasFlag(PluginCapability.LauncherCommands))
            .Select(plugin => QueryPluginAsync(plugin, query, cancellationToken))
            .ToArray();

        if (queryTasks.Length == 0)
        {
            return [];
        }

        var results = await Task.WhenAll(queryTasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return results.SelectMany(static commands => commands).ToArray();
    }

    public ValueTask SuspendAsync(CancellationToken cancellationToken = default) =>
        ChangeLifecycleStateAsync(
            PluginRuntimeState.Active,
            PluginRuntimeState.Suspended,
            "Suspend",
            static (plugin, token) => plugin.SuspendAsync(token),
            cancellationToken);

    public ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
        ChangeLifecycleStateAsync(
            PluginRuntimeState.Suspended,
            PluginRuntimeState.Active,
            "Resume",
            static (plugin, token) => plugin.ResumeAsync(token),
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var plugin in _plugins)
            {
                await RunDisposeAsync(plugin).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask ChangeLifecycleStateAsync(
        PluginRuntimeState requiredState,
        PluginRuntimeState successState,
        string operation,
        Func<ISeanShellPlugin, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            foreach (var plugin in _plugins.Where(plugin => plugin.State == requiredState))
            {
                await RunLifecycleAsync(
                    plugin,
                    operation,
                    _options.LifecycleTimeout,
                    token => action(plugin.Instance, token),
                    successState,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<IReadOnlyList<ShellCommand>> QueryPluginAsync(
        PluginRuntime plugin,
        string query,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var commands = await RunWithTimeoutAsync(
                token => plugin.Instance.GetCommandsAsync(query, token),
                _options.CommandQueryTimeout,
                cancellationToken).ConfigureAwait(false);
            plugin.RecordOperation("Query commands", stopwatch.Elapsed);
            PublishDiagnosticsChanged();
            return commands;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            plugin.Update(PluginRuntimeState.Faulted, "Query commands", stopwatch.Elapsed, FormatError(exception));
            PublishDiagnosticsChanged();
            return [];
        }
    }

    private async Task RunLifecycleAsync(
        PluginRuntime plugin,
        string operation,
        TimeSpan timeout,
        Func<CancellationToken, ValueTask> action,
        PluginRuntimeState successState,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await RunWithTimeoutAsync(
                async token =>
                {
                    await action(token).ConfigureAwait(false);
                    return true;
                },
                timeout,
                cancellationToken).ConfigureAwait(false);
            plugin.Update(successState, operation, stopwatch.Elapsed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            plugin.Update(PluginRuntimeState.Faulted, operation, stopwatch.Elapsed, FormatError(exception));
        }

        PublishDiagnosticsChanged();
    }

    private async Task RunDisposeAsync(PluginRuntime plugin)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await RunWithTimeoutAsync(
                async token =>
                {
                    await plugin.Instance.DisposeAsync().ConfigureAwait(false);
                    return true;
                },
                _options.LifecycleTimeout,
                CancellationToken.None).ConfigureAwait(false);
            plugin.Update(PluginRuntimeState.Disposed, "Dispose", stopwatch.Elapsed, null);
        }
        catch (Exception exception)
        {
            plugin.Update(PluginRuntimeState.Disposed, "Dispose", stopwatch.Elapsed, FormatError(exception));
        }

        PublishDiagnosticsChanged();
    }

    private static async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, ValueTask<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            return await action(linkedCancellation.Token)
                .AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            linkedCancellation.Cancel();
            throw new TimeoutException($"Plugin operation exceeded the {timeout.TotalMilliseconds:F0} ms limit.");
        }
    }

    private static PluginRuntime CreateRuntime(PluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.Manifest);
        ArgumentNullException.ThrowIfNull(registration.Instance);

        ValidateManifest(registration.Manifest);
        if (!string.Equals(registration.Manifest.Id, registration.Instance.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("The plugin manifest ID must match the plugin instance ID.", nameof(registration));
        }

        if (!string.Equals(registration.Manifest.Name, registration.Instance.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("The plugin manifest name must match the plugin instance name.", nameof(registration));
        }

        return new PluginRuntime(registration.Manifest, registration.Instance);
    }

    private static void ValidateManifest(PluginManifest manifest)
    {
        if (manifest.SchemaVersion != PluginManifest.CurrentSchemaVersion)
        {
            throw new ArgumentException($"Unsupported plugin manifest schema {manifest.SchemaVersion}.", nameof(manifest));
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) ||
            manifest.Id.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            throw new ArgumentException("Plugin IDs may contain only ASCII letters, digits, dots, and hyphens.", nameof(manifest));
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Publisher))
        {
            throw new ArgumentException("Plugin name and publisher are required.", nameof(manifest));
        }

        if (!Version.TryParse(manifest.Version, out _))
        {
            throw new ArgumentException("Plugin version must use a numeric semantic version such as 1.0.0.", nameof(manifest));
        }

        if (manifest.MinimumHostApiVersion > HostApiVersion)
        {
            throw new ArgumentException(
                $"Plugin requires host API {manifest.MinimumHostApiVersion}; this host supports {HostApiVersion}.",
                nameof(manifest));
        }

        if (manifest.MinimumHostApiVersion <= 0)
        {
            throw new ArgumentException("Minimum host API version must be positive.", nameof(manifest));
        }

        const PluginCapability supportedCapabilities =
            PluginCapability.LauncherCommands | PluginCapability.BackgroundWork;
        if ((manifest.Capabilities & ~supportedCapabilities) != 0)
        {
            throw new ArgumentException("Plugin manifest declares an unknown capability.", nameof(manifest));
        }

        if (!manifest.IsBuiltIn)
        {
            throw new ArgumentException("Third-party plugin loading is disabled until signing and isolation are implemented.", nameof(manifest));
        }
    }

    private static string FormatError(Exception exception) =>
        exception is TimeoutException ? exception.Message : $"{exception.GetType().Name}: {exception.Message}";

    private void PublishDiagnosticsChanged() => DiagnosticsChanged?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class PluginRuntime(PluginManifest manifest, ISeanShellPlugin instance)
    {
        private readonly object _sync = new();
        private PluginRuntimeState _state = PluginRuntimeState.NotInitialized;
        private string _lastOperation = "Registered";
        private TimeSpan _lastDuration;
        private string? _lastError;

        public PluginManifest Manifest { get; } = manifest;

        public ISeanShellPlugin Instance { get; } = instance;

        public PluginRuntimeState State
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        public void Update(
            PluginRuntimeState state,
            string operation,
            TimeSpan duration,
            string? error)
        {
            lock (_sync)
            {
                _state = state;
                _lastOperation = operation;
                _lastDuration = duration;
                _lastError = error;
            }
        }

        public void RecordOperation(string operation, TimeSpan duration)
        {
            lock (_sync)
            {
                _lastOperation = operation;
                _lastDuration = duration;
                _lastError = null;
            }
        }

        public PluginDiagnostic CreateDiagnostic()
        {
            lock (_sync)
            {
                return new PluginDiagnostic(
                    Manifest.Id,
                    Manifest.Name,
                    Manifest.Version,
                    Manifest.Publisher,
                    Manifest.Capabilities,
                    Manifest.IsBuiltIn,
                    _state,
                    _lastOperation,
                    _lastDuration,
                    _lastError);
            }
        }
    }
}
