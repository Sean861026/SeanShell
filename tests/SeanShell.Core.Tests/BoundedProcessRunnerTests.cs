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
        var result = await BoundedProcessRunner.RunAsync(
            probe.StartInfo("[Console]::Out.Write('o' * 131072); [Console]::Error.Write('e' * 131072); exit 7"),
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
        var operation = BoundedProcessRunner.RunAsync(
            probe.StartInfo("while ($true) { Start-Sleep -Milliseconds 20 }"),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        await probe.CaptureProcessAsync(operation);

        var error = await Assert.ThrowsExactlyAsync<TimeoutException>(async () => await operation);

        StringAssert.Contains(error.Message, "3 seconds");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10));
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task CallerCancellationTerminatesChildWhileBothPipesAreBeingRead()
    {
        using var probe = new IsolatedProcessProbe();
        using var cancellation = new CancellationTokenSource();
        var operation = BoundedProcessRunner.RunAsync(
            probe.StartInfo("while ($true) { [Console]::Out.Write('out'); [Console]::Error.Write('err'); Start-Sleep -Milliseconds 10 }"),
            TimeSpan.FromSeconds(15),
            cancellation.Token);
        await probe.CaptureProcessAsync(operation);
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

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => BoundedProcessRunner.RunAsync(
            probe.StartInfo("exit 0"), TimeSpan.FromSeconds(15), cancellation.Token));

        Assert.IsFalse(File.Exists(probe.ProcessIdPath));
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
        private Process? _process;

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

        public string ProcessIdPath => Path.Combine(_directory, "child.pid");

        public ProcessStartInfo StartInfo(string script)
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
            // The child only writes its own PID to our unique temporary directory and
            // emits test data. No WSL, Docker, Git, network, or desktop state is touched.
            var fullScript = $"[IO.File]::WriteAllText('{ProcessIdPath.Replace("'", "''", StringComparison.Ordinal)}', $PID.ToString()); {script}";
            startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(fullScript)));
            return startInfo;
        }

        public async Task CaptureProcessAsync(Task operation)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.Exists(ProcessIdPath) && int.TryParse(await File.ReadAllTextAsync(ProcessIdPath), out var processId))
                {
                    _process = Process.GetProcessById(processId);
                    _ = _process.Handle;
                    return;
                }

                if (operation.IsCompleted)
                {
                    await operation;
                    Assert.Fail("The isolated probe exited before signaling readiness.");
                }

                await Task.Delay(20);
            }

            Assert.Fail("The isolated probe did not start within its test deadline.");
        }

        public async Task AssertExitedAsync()
        {
            Assert.IsNotNull(_process);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(_process.HasExited);
        }

        public void Dispose()
        {
            try
            {
                if (_process is not null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            finally
            {
                _process?.Dispose();
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
