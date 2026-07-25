using System.Text.Json;

namespace SeanShell.Plugin.DotNet;

public static class DotNetLaunchSettingsReader
{
    private const int MaximumUrls = 8;

    public static IReadOnlyList<string> ReadLocalApplicationUrls(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        var path = Path.Combine(
            Path.GetFullPath(projectDirectory),
            "Properties",
            "launchSettings.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return profiles.EnumerateObject()
                .Select(static profile => profile.Value)
                .Where(static profile => profile.ValueKind == JsonValueKind.Object)
                .SelectMany(ReadApplicationUrls)
                .Where(IsSafeLocalUrl)
                .Select(static uri => uri.AbsoluteUri.TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumUrls)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<Uri> ReadApplicationUrls(JsonElement profile)
    {
        if (!profile.TryGetProperty("applicationUrl", out var applicationUrl) ||
            applicationUrl.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        return applicationUrl.GetString()!
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value =>
                Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .Where(static uri => uri is not null)
            .Cast<Uri>();
    }

    private static bool IsSafeLocalUrl(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        uri.IsLoopback;
}
