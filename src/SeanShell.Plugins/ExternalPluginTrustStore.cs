using System.Text.Json;
using System.Text.Json.Serialization;
using SeanShell.PluginContracts;

namespace SeanShell.Plugins;

public sealed class ExternalPluginTrustStore
{
    private const PluginCapability SupportedCapabilities =
        PluginCapability.LauncherCommands | PluginCapability.BackgroundWork;

    private readonly string _backupPath;
    private readonly string _filePath;
    private readonly string _temporaryPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public ExternalPluginTrustStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _backupPath = $"{_filePath}.bak";
        _temporaryPath = $"{_filePath}.tmp";
    }

    public ExternalPluginTrustLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new ExternalPluginTrustLoadResult(new ExternalPluginTrustDocument());
        }

        if (TryRead(_filePath, out var document, out var primaryError))
        {
            return new ExternalPluginTrustLoadResult(document!);
        }

        if (TryRead(_backupPath, out document, out _))
        {
            TryRestorePrimaryFromBackup();
            return new ExternalPluginTrustLoadResult(
                document!,
                WasRecovered: true,
                Warning: $"The plugin trust file was damaged, so the last known good copy was loaded. {primaryError}");
        }

        return new ExternalPluginTrustLoadResult(
            new ExternalPluginTrustDocument(),
            Warning: $"Plugin trust could not be loaded, so no external packages are approved. {primaryError}");
    }

    public void Save(ExternalPluginTrustDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The plugin trust path must include a directory.");
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

    private void TryRestorePrimaryFromBackup()
    {
        try
        {
            File.Copy(_backupPath, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory recovery remains safe. A later successful save can repair the file.
        }
    }

    private bool TryRead(
        string path,
        out ExternalPluginTrustDocument? document,
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
            document = JsonSerializer.Deserialize<ExternalPluginTrustDocument>(stream, _jsonOptions);
            if (document is null)
            {
                throw new InvalidDataException("The plugin trust document is empty.");
            }

            Validate(document);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            document = null;
            error = exception.Message;
            return false;
        }
    }

    private static void Validate(ExternalPluginTrustDocument document)
    {
        if (document.SchemaVersion != ExternalPluginTrustDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported plugin trust schema version {document.SchemaVersion}.");
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var consent in document.EffectiveConsents)
        {
            if (consent is null)
            {
                throw new InvalidDataException("The plugin trust document contains an empty decision.");
            }

            if (string.IsNullOrWhiteSpace(consent.PluginId) ||
                consent.PluginId.Any(static character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
            {
                throw new InvalidDataException("A plugin trust entry has an invalid plugin ID.");
            }

            if (string.IsNullOrWhiteSpace(consent.PublisherCertificateSha256))
            {
                throw new InvalidDataException("A plugin trust entry has an invalid publisher fingerprint.");
            }

            var fingerprint = NormalizeFingerprint(consent.PublisherCertificateSha256);
            if (fingerprint.Length != 64 ||
                fingerprint.Any(static character => !char.IsAsciiHexDigit(character)))
            {
                throw new InvalidDataException("A plugin trust entry has an invalid publisher fingerprint.");
            }

            if (consent.GrantedCapabilities == PluginCapability.None ||
                (consent.GrantedCapabilities & ~SupportedCapabilities) != 0)
            {
                throw new InvalidDataException("A plugin trust entry has invalid capabilities.");
            }

            if (consent.GrantedAtUtc == default)
            {
                throw new InvalidDataException("A plugin trust entry has an invalid grant timestamp.");
            }

            if (!keys.Add($"{consent.PluginId}\n{fingerprint}"))
            {
                throw new InvalidDataException("The plugin trust document contains a duplicate decision.");
            }
        }
    }

    internal static string NormalizeFingerprint(string fingerprint) =>
        fingerprint
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
}
