using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeanShell.Plugin.Docker;

public static partial class DockerContainerParser
{
    public static IReadOnlyList<DockerContainerSnapshot> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var containers = new List<DockerContainerSnapshot>();
        foreach (var line in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var id = ReadString(root, "ID");
                var name = ReadString(root, "Names");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                containers.Add(new(
                    id,
                    name,
                    ReadString(root, "Image"),
                    ReadString(root, "State"),
                    ReadString(root, "Status"),
                    ParsePorts(ReadString(root, "Ports"))));
            }
            catch (JsonException)
            {
                // Ignore a malformed line while keeping valid container snapshots.
            }
        }

        return containers
            .OrderBy(static container => container.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<DockerPublishedPort> ParsePorts(string value) =>
        PublishedTcpPortRegex()
            .Matches(value)
            .Select(static match => new DockerPublishedPort(
                int.Parse(match.Groups["host"].Value),
                int.Parse(match.Groups["container"].Value)))
            .Distinct()
            .OrderBy(static port => port.HostPort)
            .ThenBy(static port => port.ContainerPort)
            .ToArray();

    [GeneratedRegex(
        @"(?:(?:(?:\d{1,3}\.){3}\d{1,3}|\[::\]|localhost):)?(?<host>\d+)->(?<container>\d+)/tcp",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PublishedTcpPortRegex();
}
