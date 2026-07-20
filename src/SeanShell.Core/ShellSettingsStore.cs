using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeanShell.Core;

public sealed class ShellSettingsStore
{
    private readonly string _backupPath;
    private readonly string _filePath;
    private readonly string _temporaryPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public ShellSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _backupPath = $"{_filePath}.bak";
        _temporaryPath = $"{_filePath}.tmp";
    }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new SettingsLoadResult(new ShellSettings());
        }

        if (TryRead(_filePath, out var settings, out var primaryError))
        {
            return new SettingsLoadResult(settings!);
        }

        if (TryRead(_backupPath, out settings, out _))
        {
            TryRestorePrimaryFromBackup();
            return new SettingsLoadResult(
                settings!,
                WasRecovered: true,
                Warning: $"The settings file was damaged, so the last known good copy was loaded. {primaryError}");
        }

        return new SettingsLoadResult(
            new ShellSettings(),
            Warning: $"Settings could not be loaded, so safe defaults are active. {primaryError}");
    }

    public void Save(ShellSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != ShellSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported settings schema version {settings.SchemaVersion}.");
        }

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The settings path must include a directory.");
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
                JsonSerializer.Serialize(stream, settings, _jsonOptions);
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
            // The in-memory recovery remains usable. A later successful save can repair the file.
        }
    }

    private bool TryRead(string path, out ShellSettings? settings, out string? error)
    {
        settings = null;
        error = null;
        if (!File.Exists(path))
        {
            error = "No recovery copy exists.";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            settings = JsonSerializer.Deserialize<ShellSettings>(stream, _jsonOptions);
            if (settings is null)
            {
                throw new InvalidDataException("The settings document is empty.");
            }

            if (settings.SchemaVersion == 1)
            {
                settings = settings with { SchemaVersion = ShellSettings.CurrentSchemaVersion };
            }
            else if (settings.SchemaVersion != ShellSettings.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported settings schema version {settings.SchemaVersion}.");
            }

            if (!Enum.IsDefined(settings.LauncherShortcut))
            {
                throw new InvalidDataException("The launcher shortcut is not supported.");
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            settings = null;
            error = exception.Message;
            return false;
        }
    }
}
