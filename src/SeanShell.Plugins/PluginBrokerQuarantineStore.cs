using System.Text.Json;

namespace SeanShell.Plugins;

public sealed class PluginBrokerQuarantineStore
{
    public const int MaximumEntries = 128;

    private readonly string _backupPath;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _temporaryPath;

    public PluginBrokerQuarantineStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _backupPath = $"{_filePath}.bak";
        _temporaryPath = $"{_filePath}.tmp";
    }

    public PluginBrokerQuarantineLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new(new PluginBrokerQuarantineDocument());
        }

        if (TryRead(_filePath, out var document, out var primaryError))
        {
            return new(document!);
        }

        if (TryRead(_backupPath, out document, out _))
        {
            TryRestorePrimaryFromBackup();
            return new(
                document!,
                WasRecovered: true,
                Warning:
                    $"Broker quarantine history was damaged, so the last known good copy was loaded. {primaryError}");
        }

        return new(
            new PluginBrokerQuarantineDocument(),
            PersistenceAvailable: false,
            Warning:
                $"Broker quarantine history is unavailable; external broker probes are blocked. {primaryError}");
    }

    public void Save(PluginBrokerQuarantineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException(
                "The broker quarantine path must include a directory.");
        Directory.CreateDirectory(directory);

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

    private bool TryRead(
        string path,
        out PluginBrokerQuarantineDocument? document,
        out string? error)
    {
        document = null;
        error = null;
        if (!File.Exists(path))
        {
            error = "No recovery copy exists.";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize<PluginBrokerQuarantineDocument>(
                stream,
                _jsonOptions);
            if (document is null)
            {
                throw new InvalidDataException(
                    "The broker quarantine document is empty.");
            }

            Validate(document);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidDataException)
        {
            document = null;
            error = exception.Message;
            return false;
        }
    }

    private void TryRestorePrimaryFromBackup()
    {
        try
        {
            File.Copy(_backupPath, _filePath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The valid in-memory recovery remains fail-closed.
        }
    }

    private static void Validate(PluginBrokerQuarantineDocument document)
    {
        if (document.SchemaVersion != PluginBrokerQuarantineDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported broker quarantine schema version {document.SchemaVersion}.");
        }

        if (document.EffectiveEntries.Count > MaximumEntries)
        {
            throw new InvalidDataException(
                $"Broker quarantine history may not exceed {MaximumEntries} entries.");
        }

        var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.EffectiveEntries)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.PluginId) ||
                entry.PluginId.Any(static character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
            {
                throw new InvalidDataException(
                    "Broker quarantine history contains an invalid plugin ID.");
            }

            if (!pluginIds.Add(entry.PluginId))
            {
                throw new InvalidDataException(
                    "Broker quarantine history contains duplicate plugin IDs.");
            }

            if (entry.ConsecutiveFailures is <= 0 or > PluginBrokerQuarantineManager.FailureThreshold ||
                entry.WindowStartedAtUtc == default ||
                entry.LastFailureAtUtc < entry.WindowStartedAtUtc ||
                entry.LastFailureAtUtc - entry.WindowStartedAtUtc >
                    PluginBrokerQuarantineManager.FailureWindow ||
                entry.QuarantinedUntilUtc < entry.LastFailureAtUtc ||
                entry.QuarantinedUntilUtc - entry.LastFailureAtUtc >
                    PluginBrokerQuarantineManager.QuarantineDuration ||
                (entry.ConsecutiveFailures == PluginBrokerQuarantineManager.FailureThreshold) !=
                    (entry.QuarantinedUntilUtc is not null))
            {
                throw new InvalidDataException(
                    "Broker quarantine history contains an invalid failure window.");
            }
        }
    }
}
