using System.Text.Json;
using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DiagnosticJournalTests
{
    [TestMethod]
    [DataRow(TaskbarFailureStage.GuardReadiness, TaskbarFailureReason.TimedOut)]
    [DataRow(TaskbarFailureStage.GuardReadiness, TaskbarFailureReason.HandshakeMismatch)]
    [DataRow(TaskbarFailureStage.GuardReadiness, TaskbarFailureReason.NotReady)]
    [DataRow(TaskbarFailureStage.GuardStart, TaskbarFailureReason.ProcessNotCreated)]
    [DataRow(TaskbarFailureStage.GuardStart, TaskbarFailureReason.Exception)]
    [DataRow(TaskbarFailureStage.Hide, TaskbarFailureReason.RequestRejected)]
    [DataRow(TaskbarFailureStage.Hide, TaskbarFailureReason.VisibilityTimedOut)]
    [DataRow(TaskbarFailureStage.Show, TaskbarFailureReason.NoTaskbars)]
    [DataRow(TaskbarFailureStage.WorkAreaReserve, TaskbarFailureReason.ReservationRejected)]
    [DataRow(TaskbarFailureStage.WorkAreaRelease, TaskbarFailureReason.ReservationRejected)]
    public void TaskbarFailuresContainOnlyTypedStageReasonCountsAndSafeErrors(
        TaskbarFailureStage stage,
        TaskbarFailureReason reason)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path);
        var exception = new IOException("private-executable-path-and-guard-stdout");
        exception.Data["command-line"] = "private-arguments";

        Assert.IsTrue(journal.TryWriteTaskbarFailure(stage, reason, 2, 1, exception));

        var text = File.ReadAllText(path);
        Assert.IsFalse(text.Contains("private-", StringComparison.Ordinal));
        using var entry = JsonDocument.Parse(text);
        Assert.AreEqual("TaskbarOperationFailed", entry.RootElement.GetProperty("Event").GetString());
        var taskbar = entry.RootElement.GetProperty("Taskbar");
        Assert.AreEqual(stage.ToString(), taskbar.GetProperty("Stage").GetString());
        Assert.AreEqual(reason.ToString(), taskbar.GetProperty("Reason").GetString());
        Assert.AreEqual(2, taskbar.GetProperty("TargetCount").GetInt32());
        Assert.AreEqual(1, taskbar.GetProperty("AffectedCount").GetInt32());
        Assert.AreEqual(4, taskbar.EnumerateObject().Count());
        Assert.AreEqual(exception.HResult,
            entry.RootElement.GetProperty("Errors")[0].GetProperty("HResult").GetInt32());
    }

    [TestMethod]
    public void UnknownTaskbarEnumsAndNegativeCountsAreNormalized()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path);

        Assert.IsTrue(journal.TryWriteTaskbarFailure(
            (TaskbarFailureStage)int.MaxValue,
            (TaskbarFailureReason)int.MaxValue,
            -1,
            -2));

        using var entry = JsonDocument.Parse(File.ReadAllText(path));
        var taskbar = entry.RootElement.GetProperty("Taskbar");
        Assert.AreEqual("Unknown", taskbar.GetProperty("Stage").GetString());
        Assert.AreEqual("Unknown", taskbar.GetProperty("Reason").GetString());
        Assert.AreEqual(0, taskbar.GetProperty("TargetCount").GetInt32());
        Assert.AreEqual(0, taskbar.GetProperty("AffectedCount").GetInt32());
    }

    [TestMethod]
    public void TaskbarDiagnosticStorageFailureDoesNotEscape()
    {
        using var directory = new TemporaryDirectory();
        var parent = Path.Combine(directory.Path, "file-not-directory");
        File.WriteAllText(parent, "unchanged");
        var journal = new DiagnosticJournal(Path.Combine(parent, "events.jsonl"));

        Assert.IsFalse(journal.TryWriteTaskbarFailure(
            TaskbarFailureStage.Hide, TaskbarFailureReason.RequestRejected, 2, 1));
        Assert.AreEqual("unchanged", File.ReadAllText(parent));
    }

    [TestMethod]
    public void NonTaskbarDiagnosticsKeepExistingSchemaWithoutTaskbarPayload()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path);

        Assert.IsTrue(journal.TryWrite(DiagnosticEventKind.SessionStarted));

        using var entry = JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual(1, entry.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.IsFalse(entry.RootElement.TryGetProperty("Taskbar", out _));
    }

    [TestMethod]
    [DataRow(DiagnosticEventKind.DockActionFailed)]
    [DataRow(DiagnosticEventKind.LauncherSearchFailed)]
    [DataRow(DiagnosticEventKind.LauncherActionFailed)]
    [DataRow(DiagnosticEventKind.LauncherActivationFailed)]
    public void WritesSessionMetadataAndErrorsWithoutMessagesOrData(DiagnosticEventKind kind)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path);
        var exception = new InvalidOperationException(
            "private-search-query", new IOException("private-file-path"));
        exception.Data["secret"] = "wifi-password";

        Assert.IsTrue(journal.TryWrite(kind, exception));

        var text = File.ReadAllText(path);
        Assert.IsFalse(text.Contains("private-search-query", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("private-file-path", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("wifi-password", StringComparison.Ordinal));
        using var entry = JsonDocument.Parse(text);
        Assert.AreEqual(kind.ToString(), entry.RootElement.GetProperty("Event").GetString());
        Assert.AreEqual(2, entry.RootElement.GetProperty("Errors").GetArrayLength());
        Assert.AreEqual(exception.HResult,
            entry.RootElement.GetProperty("Errors")[0].GetProperty("HResult").GetInt32());
        Assert.AreNotEqual(Guid.Empty, entry.RootElement.GetProperty("SessionId").GetGuid());
    }

    [TestMethod]
    public void RotatesToOnePreviousFileWithinBudget()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path, maximumFileBytes: 1024);
        for (var index = 0; index < 50; index++)
        {
            Assert.IsTrue(journal.TryWrite(DiagnosticEventKind.SessionStarted));
        }

        Assert.IsLessThanOrEqualTo(1024L, new FileInfo(path).Length);
        Assert.IsLessThanOrEqualTo(1024L, new FileInfo(path + ".previous").Length);
        Assert.HasCount(2, Directory.GetFiles(directory.Path));
        foreach (var file in Directory.GetFiles(directory.Path))
        {
            foreach (var line in File.ReadLines(file))
            {
                using var entry = JsonDocument.Parse(line);
                Assert.AreEqual(1, entry.RootElement.GetProperty("SchemaVersion").GetInt32());
            }
        }
    }

    [TestMethod]
    public void ConcurrentWritesRemainCompleteJsonLines()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path);
        Parallel.For(0, 100, _ =>
            Assert.IsTrue(journal.TryWrite(DiagnosticEventKind.StartupHealthy)));

        var lines = File.ReadAllLines(path);
        Assert.HasCount(100, lines);
        var sessions = lines.Select(static line =>
        {
            using var entry = JsonDocument.Parse(line);
            return entry.RootElement.GetProperty("SessionId").GetGuid();
        });
        Assert.AreEqual(1, sessions.Distinct().Count());
    }

    [TestMethod]
    public void StorageFailureDoesNotEscape()
    {
        using var directory = new TemporaryDirectory();
        var parent = Path.Combine(directory.Path, "file-not-directory");
        File.WriteAllText(parent, "unchanged");
        var journal = new DiagnosticJournal(Path.Combine(parent, "events.jsonl"));

        Assert.IsFalse(journal.TryWrite(DiagnosticEventKind.UnhandledUiException,
            new InvalidOperationException("sensitive-message")));
        Assert.AreEqual("unchanged", File.ReadAllText(parent));
    }

    [TestMethod]
    public void InnerErrorChainIsBounded()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path, maximumFileBytes: 1024);
        var exception = new InvalidOperationException("ignored");
        for (var index = 0; index < 4; index++)
        {
            exception = new InvalidOperationException("ignored", exception);
        }

        // The inner chain is bounded even when supplied by an unexpected provider.
        Assert.IsTrue(journal.TryWrite(DiagnosticEventKind.DockActionFailed, exception));
        using var entry = JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual(4, entry.RootElement.GetProperty("Errors").GetArrayLength());
    }

    [TestMethod]
    public void OversizedEntryIsRejectedWithoutGrowingFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path, maximumFileBytes: 1024);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            ThrowForDiagnosticStack(20));
        for (var index = 0; index < 3; index++)
        {
            try
            {
                throw new InvalidOperationException("private-inner-value", error);
            }
            catch (InvalidOperationException outer)
            {
                error = outer;
            }
        }

        Assert.IsFalse(journal.TryWrite(DiagnosticEventKind.DockActionFailed, error));
        Assert.IsFalse(File.Exists(path));
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int ThrowForDiagnosticStack(int depth) => depth > 0
        ? ThrowForDiagnosticStack(depth - 1) + 1
        : throw new InvalidOperationException("private-value");

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory =
            Directory.CreateTempSubdirectory("SeanShell-diagnostics-tests-");

        public string Path => _directory.FullName;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
