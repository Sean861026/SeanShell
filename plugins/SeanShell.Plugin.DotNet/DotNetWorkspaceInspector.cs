using System.Xml;
using System.Xml.Linq;

namespace SeanShell.Plugin.DotNet;

public static class DotNetWorkspaceInspector
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            "bin",
            "node_modules",
            "obj",
        };

    public static DotNetWorkspaceSnapshot? Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is ".sln" or ".slnx")
        {
            return new(
                fullPath,
                Path.GetFileNameWithoutExtension(fullPath),
                ".NET solution",
                [],
                true);
        }

        if (!string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var reader = XmlReader.Create(
                fullPath,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                });
            var document = XDocument.Load(reader, LoadOptions.None);
            var sdkValues = new[] { document.Root?.Attribute("Sdk")?.Value }
                .Concat(document.Root?.Elements()
                    .Where(static element => element.Name.LocalName == "Sdk")
                    .Select(static element => element.Attribute("Name")?.Value)
                    ?? [])
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>();
            var sdk = string.Join(";", sdkValues);
            var targetFrameworks = ReadTargetFrameworks(document);
            var packageReferences = ReadItemNames(document, "PackageReference");
            var frameworkReferences = ReadItemNames(document, "FrameworkReference");
            var projectDirectory = Path.GetDirectoryName(fullPath)!;
            var projectType = Classify(
                document,
                sdk,
                packageReferences,
                frameworkReferences,
                projectDirectory);

            return new(
                fullPath,
                Path.GetFileNameWithoutExtension(fullPath),
                projectType,
                targetFrameworks,
                false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(XDocument document) =>
        document.Descendants()
            .Where(static element =>
                element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(static element => element.Value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> ReadItemNames(XDocument document, string itemName) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(static element =>
                element.Attribute("Include")?.Value ??
                element.Attribute("Update")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

    private static string Classify(
        XDocument document,
        string sdk,
        IReadOnlyList<string> packageReferences,
        IReadOnlyList<string> frameworkReferences,
        string projectDirectory)
    {
        if (Contains(sdk, "Microsoft.NET.Sdk.BlazorWebAssembly") ||
            packageReferences.Any(static package =>
                Contains(package, "Microsoft.AspNetCore.Components.WebAssembly")))
        {
            return "Blazor WebAssembly";
        }

        if (HasBooleanProperty(document, "UseMaui"))
        {
            return ".NET MAUI";
        }

        if (Contains(sdk, "Microsoft.NET.Sdk.Web") ||
            frameworkReferences.Any(static framework =>
                Contains(framework, "Microsoft.AspNetCore.App")))
        {
            return ContainsRazorFile(projectDirectory)
                ? "ASP.NET Core / Blazor"
                : "ASP.NET Core";
        }

        if (Contains(sdk, "Microsoft.NET.Sdk.Worker"))
        {
            return ".NET Worker";
        }

        if (Contains(sdk, "Microsoft.NET.Sdk.Razor") ||
            ContainsRazorFile(projectDirectory))
        {
            return "Razor";
        }

        return "C#";
    }

    private static bool HasBooleanProperty(XDocument document, string propertyName) =>
        document.Descendants()
            .Any(element =>
                element.Name.LocalName == propertyName &&
                bool.TryParse(element.Value, out var value) &&
                value);

    private static bool ContainsRazorFile(string root)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.Count > 0)
        {
            var candidate = pending.Dequeue();
            try
            {
                if (Directory.EnumerateFiles(
                        candidate.Path,
                        "*.razor",
                        SearchOption.TopDirectoryOnly)
                    .Any())
                {
                    return true;
                }

                if (candidate.Depth >= 3)
                {
                    continue;
                }

                foreach (var directory in Directory.EnumerateDirectories(candidate.Path))
                {
                    if (!ExcludedDirectoryNames.Contains(Path.GetFileName(directory)) &&
                        !File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                    {
                        pending.Enqueue((directory, candidate.Depth + 1));
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    private static bool Contains(string value, string expected) =>
        value.Contains(expected, StringComparison.OrdinalIgnoreCase);
}
