using System.Diagnostics;
using System.Text;

namespace SeanShell.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BoundedProcessRunnerTests
{
    [TestMethod]
    public async Task DrainsBothPipesBeforeReturningExitCode()
    {
        using var probe = new IsolatedProcessProbe();
        var result = await probe.RunAsync(
            "[Console]::Out.Write('o' * 131072); [Console]::Error.Write('e' * 131072); exit 7",
            TimeSpan.FromSeconds(15),
            CancellationToken.None);

        Assert.AreEqual(7, result.ExitCode);
        Assert.AreEqual(new string('o', 131072), result.StandardOutput);
        Assert.AreEqual(new string('e', 131072), result.StandardError);
    }

    [TestMethod]
    public async Task DeadlineWithoutCallerTokenTerminatesNeverExitingChild()
    {
        using var probe = new IsolatedProcessProbe();
        var watch = Stopwatch.StartNew();
        var operation = probe.RunAsync(
            "while ($true) { Start-Sleep -Milliseconds 20 }",
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        await probe.CaptureProcessAsync();

        var error = await Assert.ThrowsExactlyAsync<TimeoutException>(async () => await operation);

        StringAssert.Contains(error.Message, "3 seconds");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10));
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task DeadlineCleansUpBeforeInterpreterReadinessAndLateObservation()
    {
        using var probe = new IsolatedProcessProbe();
        // Model a cold interpreter that cannot reach readiness before the unchanged
        // deadline. The OS process is captured at Start, not through a PID file.
        var operation = probe.RunAsync(
            "while ($true) { Start-Sleep -Milliseconds 20 }",
            TimeSpan.FromSeconds(3),
            CancellationToken.None,
            startupScript: "Start-Sleep -Seconds 30;");

        await Assert.ThrowsExactlyAsync<TimeoutException>(async () => await operation);
        await probe.CaptureProcessAsync();
        await probe.AssertExitedAsync();

        Assert.IsFalse(File.Exists(probe.ReadyPath));
        // Re-observe after cleanup definitely finished: the retained handle still
        // identifies this exact child even though a fresh PID lookup would fail.
        await probe.CaptureProcessAsync();
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task CallerCancellationTerminatesChildWhileBothPipesAreBeingRead()
    {
        using var probe = new IsolatedProcessProbe();
        using var cancellation = new CancellationTokenSource();
        var queryDeadline = TimeSpan.FromSeconds(15);
        var operation = probe.RunAsync(
            "while ($true) { [Console]::Out.Write('out'); [Console]::Error.Write('err'); Start-Sleep -Milliseconds 10 }",
            queryDeadline,
            cancellation.Token);
        await probe.CaptureProcessAsync(operation, queryDeadline);
        await probe.WaitForInterpreterReadinessAsync(operation);
        var watch = Stopwatch.StartNew();
        cancellation.Cancel();

        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await operation);

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5));
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task CanceledCallerDoesNotStartProcess()
    {
        using var probe = new IsolatedProcessProbe();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => probe.RunAsync(
            "exit 0", TimeSpan.FromSeconds(15), cancellation.Token));

        Assert.IsFalse(probe.HasStarted);
        Assert.IsFalse(File.Exists(probe.ReadyPath));
    }

    [TestMethod]
    public void InfiniteOrNonPositiveDeadlineIsRejected()
    {
        var startInfo = new ProcessStartInfo("unused-query")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var invalid in new[] { Timeout.InfiniteTimeSpan, TimeSpan.Zero, TimeSpan.MaxValue })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                BoundedProcessRunner.RunAsync(startInfo, invalid, CancellationToken.None));
        }
    }

    [TestMethod]
    public void OwnedDirectoryCleanupRetriesTransientIoFailures()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        DeleteOwnedDirectoryWithRetry(() =>
        {
            if (++attempts < 3)
            {
                throw new IOException("The exited child still holds its current-directory handle.");
            }
        }, delays.Add);

        Assert.AreEqual(3, attempts);
        CollectionAssert.AreEqual(
            new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100) },
            delays);
    }

    [TestMethod]
    public void OwnedDirectoryCleanupHasFiniteRetriesAndPreservesLastFailure()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var expected = new IOException("The directory remains locked.");

        var actual = Assert.ThrowsExactly<IOException>(() => DeleteOwnedDirectoryWithRetry(() =>
        {
            attempts++;
            throw expected;
        }, delays.Add));

        Assert.AreSame(expected, actual);
        Assert.AreEqual(50, attempts);
        Assert.HasCount(49, delays);
        Assert.AreEqual(TimeSpan.FromMilliseconds(4900), TimeSpan.FromTicks(delays.Sum(delay => delay.Ticks)));
    }

    [TestMethod]
    public void OwnedDirectoryCleanupDoesNotHideNonIoFailures()
    {
        var expected = new UnauthorizedAccessException("Cleanup target is not accessible.");
        var delays = new List<TimeSpan>();

        var actual = Assert.ThrowsExactly<UnauthorizedAccessException>(() => DeleteOwnedDirectoryWithRetry(
            () => throw expected,
            delays.Add));

        Assert.AreSame(expected, actual);
        Assert.HasCount(0, delays);
    }

    [TestMethod]
    public async Task ProcessStartupCanTakeSixVirtualSecondsWithinItsQueryBudget()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new StartupTimeProvider();
        var waiting = WaitForProcessStartAsync(started.Task, operation.Task, TimeSpan.FromSeconds(15), time);

        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(15) }, time.ScheduledDelays);
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.AreEqual(0, time.FiredTimers);
        Assert.IsFalse(waiting.IsCompleted);
        started.SetResult();

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(operation.Task.IsCompleted);
    }

    [TestMethod]
    public async Task ProcessStartupWatchdogExpiresAtItsQueryBudget()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new StartupTimeProvider();
        var waiting = WaitForProcessStartAsync(started.Task, operation.Task, TimeSpan.FromSeconds(15), time);

        time.Advance(TimeSpan.FromSeconds(14));
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(0, time.FiredTimers);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, time.FiredTimers);
        Assert.AreSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(5))));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => waiting);
    }

    [TestMethod]
    public async Task ProcessStartupPreservesEarlyOperationFailure()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("The probe failed before reporting process startup.");
        var time = new StartupTimeProvider();
        var waiting = WaitForProcessStartAsync(started.Task, operation.Task, TimeSpan.FromSeconds(15), time);

        operation.SetException(expected);

        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreSame(expected, actual);
        Assert.AreEqual(0, time.FiredTimers);
        Assert.IsFalse(started.Task.IsCompleted);
    }

    [TestMethod]
    public async Task ProcessStartupRejectsOperationCompletionWithoutStartedSignal()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new StartupTimeProvider();
        var waiting = WaitForProcessStartAsync(started.Task, operation.Task, TimeSpan.FromSeconds(15), time);

        operation.SetResult();

        var error = await Assert.ThrowsExactlyAsync<AssertFailedException>(
            () => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        StringAssert.Contains(error.Message, "without reporting process startup");
        Assert.AreEqual(0, time.FiredTimers);
    }

    private static async Task WaitForProcessStartAsync(
        Task started,
        Task operation,
        TimeSpan setupBudget,
        TimeProvider? timeProvider = null)
    {
        // Setup is not the behavior measured by the caller-cancellation test. Use its
        // existing query deadline, while surfacing a failed Start/observer immediately.
        await Task.WhenAny(started, operation)
            .WaitAsync(setupBudget, timeProvider ?? TimeProvider.System);
        if (started.IsCompleted)
        {
            await started;
            return;
        }

        await operation;
        Assert.Fail("The query operation completed without reporting process startup.");
    }

    private static void DeleteOwnedDirectoryWithRetry(Action delete, Action<TimeSpan> delay)
    {
        // A signaled process handle does not guarantee immediate filesystem-handle release
        // on Windows/CI. Retry only this fixture's deletion, with at most 4.9 seconds of
        // short backoffs. The final IOException remains visible; never silently leak data.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                delete();
                return;
            }
            catch (IOException) when (attempt < 50)
            {
                delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private sealed class IsolatedProcessProbe : IDisposable
    {
        private readonly string _directory;
        private readonly string _executable;
        private readonly object _gate = new();
        private readonly TaskCompletionSource<Process> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Process? _process;
        private bool _disposed;

        public IsolatedProcessProbe()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("The isolated child-process probe uses Windows PowerShell, like the Windows CI runner.");
            }

            _executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.IsTrue(File.Exists(_executable));
            _directory = Path.Combine(Path.GetTempPath(), "SeanShell-process-query-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public string ReadyPath => Path.Combine(_directory, "ready");

        public bool HasStarted => _started.Task.IsCompletedSuccessfully;

        public Task<ProcessExecutionResult> RunAsync(
            string script,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string startupScript = "")
        {
            var startInfo = new ProcessStartInfo(_executable)
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = _directory,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-EncodedCommand");
            // The child only writes readiness to our unique temporary directory and
            // emits test data. No WSL, Docker, Git, network, or desktop state is touched.
            var fullScript = $"{startupScript} [IO.File]::WriteAllText('{ReadyPath.Replace("'", "''", StringComparison.Ordinal)}', 'ready'); {script}";
            startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(fullScript)));
            return BoundedProcessRunner.RunAsync(startInfo, timeout, cancellationToken, OnProcessStarted);
        }

        private void OnProcessStarted(Process process)
        {
            // This runs synchronously before the runner can kill/dispose its Process.
            // Acquire a separate retained handle now, never look up a PID after cleanup.
            var owned = Process.GetProcessById(process.Id);
            try
            {
                _ = owned.Handle;
                Assert.AreEqual(process.StartTime.ToUniversalTime(), owned.StartTime.ToUniversalTime());
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        _process = owned;
                        _started.TrySetResult(owned);
                        return;
                    }
                }

                // If the fixture failed/disposed while Start was delayed, clean up the
                // exact process immediately instead of waiting for its query deadline.
                Terminate(owned);
                _started.TrySetCanceled();
            }
            catch
            {
                owned.Dispose();
                throw;
            }

            owned.Dispose();
        }

        public async Task CaptureProcessAsync() =>
            _ = await _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task CaptureProcessAsync(Task operation, TimeSpan setupBudget) =>
            WaitForProcessStartAsync(_started.Task, operation, setupBudget);

        public async Task WaitForInterpreterReadinessAsync(Task operation)
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(ReadyPath) && watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (operation.IsCompleted)
                {
                    await operation;
                    Assert.Fail("The isolated probe exited before signaling readiness.");
                }

                await Task.Delay(20);
            }

            Assert.IsTrue(File.Exists(ReadyPath), "The isolated interpreter never became ready.");
        }

        public async Task AssertExitedAsync()
        {
            var process = await _started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(process.HasExited);
        }

        public void Dispose()
        {
            Process? process;
            lock (_gate)
            {
                _disposed = true;
                process = _process;
                _process = null;
            }

            try
            {
                if (process is not null)
                {
                    Terminate(process);
                }
            }
            finally
            {
                process?.Dispose();
                DeleteOwnedDirectoryWithRetry(
                    () => Directory.Delete(_directory, recursive: true),
                    Thread.Sleep);
            }
        }

        private static void Terminate(Process process)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                Assert.IsTrue(process.WaitForExit(5000), "The owned probe did not terminate during cleanup.");
            }
        }
    }

    // The existing broker-test clock only overrides GetUtcNow; startup regression
    // tests need one-shot timers for Task.WaitAsync without real six/fifteen-second sleeps.
    private sealed class StartupTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<StartupTimer> _timers = [];
        private TimeSpan _elapsed;

        public List<TimeSpan> ScheduledDelays { get; } = [];

        public int FiredTimers { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new StartupTimer(this, callback, state);
            lock (_gate)
            {
                _timers.Add(timer);
                ScheduledDelays.Add(dueTime);
                timer.Change(dueTime, period);
            }

            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            var due = new List<StartupTimer>();
            lock (_gate)
            {
                _elapsed += elapsed;
                foreach (var timer in _timers)
                {
                    if (!timer.Disposed && timer.DueAt is { } dueAt && dueAt <= _elapsed)
                    {
                        timer.DueAt = null;
                        FiredTimers++;
                        due.Add(timer);
                    }
                }
            }

            foreach (var timer in due)
            {
                timer.Callback(timer.State);
            }
        }

        private sealed class StartupTimer(StartupTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public TimerCallback Callback { get; } = callback;
            public object? State { get; } = state;
            public TimeSpan? DueAt { get; set; }
            public bool Disposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.AreEqual(Timeout.InfiniteTimeSpan, period, "The startup watchdog must be a one-shot timer.");
                lock (owner._gate)
                {
                    if (Disposed)
                    {
                        return false;
                    }

                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._elapsed + dueTime;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner._gate)
                {
                    Disposed = true;
                    DueAt = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
