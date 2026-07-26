using System.Text.Json;

namespace SeanShell.Gaming;

public sealed class GamingSessionStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _backupPath;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _temporaryPath;

    public GamingSessionStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _backupPath = $"{_filePath}.bak";
        _temporaryPath = $"{_filePath}.tmp";
    }

    public GamingSessionLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new GamingSessionLoadResult([]);
        }

        if (TryRead(_filePath, out var sessions, out var primaryError))
        {
            return new GamingSessionLoadResult(sessions!);
        }

        if (TryRead(_backupPath, out sessions, out _))
        {
            TryRestorePrimaryFromBackup();
            return new GamingSessionLoadResult(
                sessions!,
                WasRecovered: true,
                Warning: $"Gaming session history was recovered from its backup. {primaryError}");
        }

        return new GamingSessionLoadResult(
            [],
            Warning: $"Gaming session history could not be loaded. New sessions can still be recorded. {primaryError}");
    }

    public void Save(IReadOnlyList<GamingSessionRecord> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The gaming session path must include a directory.");
        Directory.CreateDirectory(directory);
        var document = new GamingSessionDocument(CurrentSchemaVersion, sessions);

        try
        {
            using (var stream = new FileStream(
                _temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, document, _jsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_filePath))
            {
                ReplaceExistingFile();
            }
            else
            {
                File.Move(_temporaryPath, _filePath);
                File.Copy(_filePath, _backupPath, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(_temporaryPath))
            {
                File.Delete(_temporaryPath);
            }
        }
    }

    private void ReplaceExistingFile()
    {
        try
        {
            File.Replace(_temporaryPath, _filePath, _backupPath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(_filePath, _backupPath, overwrite: true);
            File.Move(_temporaryPath, _filePath, overwrite: true);
        }
    }

    private void TryRestorePrimaryFromBackup()
    {
        try
        {
            File.Copy(_backupPath, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The recovered in-memory history remains available.
        }
    }

    private bool TryRead(
        string path,
        out IReadOnlyList<GamingSessionRecord>? sessions,
        out string? error)
    {
        sessions = null;
        error = null;
        if (!File.Exists(path))
        {
            error = "No recovery copy exists.";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<GamingSessionDocument>(stream, _jsonOptions)
                ?? throw new InvalidDataException("The gaming session document is empty.");
            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported gaming session schema version {document.SchemaVersion}.");
            }

            var storedSessions = document.Sessions
                ?? throw new InvalidDataException("The gaming session list is missing.");
            sessions = storedSessions
                .Where(IsValid)
                .OrderByDescending(static session => session.EndedAt)
                .ToArray();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsValid(GamingSessionRecord session) =>
        session.Id != Guid.Empty &&
        session.EndedAt >= session.StartedAt &&
        session.GameNames is { Count: > 0 };

    private sealed record GamingSessionDocument(
        int SchemaVersion,
        IReadOnlyList<GamingSessionRecord> Sessions);
}
