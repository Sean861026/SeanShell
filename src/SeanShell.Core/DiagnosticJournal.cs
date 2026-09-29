using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SeanShell.Core;

public enum DiagnosticEventKind
{
    SessionStarted,
    StartupBlocked,
    StartupHealthy,
    CleanExit,
    UnhandledUiException,
    UnhandledProcessException,
    UnobservedTaskException,
    DockActivationFailed,
    DockActionFailed,
}

/// <summary>
/// Best-effort, bounded local diagnostics. Exception messages, data, file paths,
/// user input, window titles, and command arguments are deliberately excluded.
/// </summary>
public sealed class DiagnosticJournal
{
    public const int DefaultMaximumFileBytes = 128 * 1024;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _maximumFileBytes;
    private readonly string _buildVersion;
    private readonly Guid _sessionId = Guid.NewGuid();

    public DiagnosticJournal(
        string path,
        int maximumFileBytes = DefaultMaximumFileBytes,
        string? buildVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumFileBytes < 1024 || maximumFileBytes > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        _path = Path.GetFullPath(path);
        _maximumFileBytes = maximumFileBytes;
        _buildVersion = Limit(buildVersion ?? "unknown");
    }

    public bool TryWrite(DiagnosticEventKind kind, Exception? exception = null)
    {
        try
        {
            var errors = new List<ErrorSummary>();
            for (var current = exception; current is not null && errors.Count < 4;
                current = current.InnerException)
            {
                var methods = new StackTrace(current, false).GetFrames()
                    .Take(8)
                    .Select(static frame => frame.GetMethod())
                    .Where(static method => method is not null)
                    .Select(static method => Limit(
                        $"{method!.DeclaringType?.FullName}.{method.Name}"))
                    .ToArray();
                errors.Add(new ErrorSummary(
                    Limit(current.GetType().FullName ?? current.GetType().Name),
                    current.HResult,
                    methods));
            }

            var entry = new DiagnosticEntry(
                1, DateTimeOffset.UtcNow, _sessionId, Environment.ProcessId, _buildVersion,
                Enum.IsDefined(kind) ? kind.ToString() : "Unknown", errors.ToArray());
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
            if (bytes.Length > _maximumFileBytes)
            {
                return false;
            }

            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) &&
                    new FileInfo(_path).Length + bytes.Length > _maximumFileBytes)
                {
                    // Keep only one previous file: at most twice the configured budget.
                    File.Move(_path, _path + ".previous", overwrite: true);
                }

                using var stream = new FileStream(
                    _path, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }

            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Diagnostics must never become a second source of application failures.
            return false;
        }
    }

    private static string Limit(string value) => value.Length <= 256 ? value : value[..256];

    private sealed record ErrorSummary(string Type, int HResult, string[] Methods);

    private sealed record DiagnosticEntry(
        int SchemaVersion,
        DateTimeOffset TimestampUtc,
        Guid SessionId,
        int ProcessId,
        string BuildVersion,
        string Event,
        ErrorSummary[] Errors);
}
