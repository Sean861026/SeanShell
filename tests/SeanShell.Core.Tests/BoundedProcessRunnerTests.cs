using System.Diagnostics;
using System.Text;

namespace SeanShell.Core.Tests;

[TestClass]
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
        var operation = probe.RunAsync(
            "while ($true) { [Console]::Out.Write('out'); [Console]::Error.Write('err'); Start-Sleep -Milliseconds 10 }",
            TimeSpan.FromSeconds(15),
            cancellation.Token);
        await probe.CaptureProcessAsync();
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
                Directory.Delete(_directory, recursive: true);
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
}
