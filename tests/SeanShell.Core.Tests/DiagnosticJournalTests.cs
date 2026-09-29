using System.Text.Json;
using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DiagnosticJournalTests
{
    [TestMethod]
    public void WritesSessionMetadataAndErrorsWithoutMessagesOrData()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var journal = new DiagnosticJournal(path);
        var exception = new InvalidOperationException(
            "private-search-query", new IOException("private-file-path"));
        exception.Data["secret"] = "wifi-password";

        Assert.IsTrue(journal.TryWrite(DiagnosticEventKind.DockActionFailed, exception));

        var text = File.ReadAllText(path);
        Assert.IsFalse(text.Contains("private-search-query", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("private-file-path", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("wifi-password", StringComparison.Ordinal));
        using var entry = JsonDocument.Parse(text);
        Assert.AreEqual("DockActionFailed", entry.RootElement.GetProperty("Event").GetString());
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
