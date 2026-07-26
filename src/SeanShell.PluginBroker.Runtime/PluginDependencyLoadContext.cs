using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using SeanShell.PluginBroker.Protocol;

namespace SeanShell.PluginBroker.Runtime;

public sealed class PluginDependencyLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly string _packageDirectory;
    private readonly IReadOnlyDictionary<string, DependencyFile> _managedDependencies;
    private readonly IReadOnlyDictionary<string, DependencyFile> _nativeDependencies;
    private readonly IReadOnlyDictionary<string, Assembly> _sharedAssemblies;
    private readonly HashSet<string> _frameworkAssemblies;

    public PluginDependencyLoadContext(
        string packageDirectory,
        IEnumerable<PluginBrokerDependency> dependencies,
        IEnumerable<Assembly>? sharedAssemblies = null)
        : base($"SeanShell.Plugin.{Guid.NewGuid():N}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(dependencies);

        _packageDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(packageDirectory));
        if (!Directory.Exists(_packageDirectory) ||
            (File.GetAttributes(_packageDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The plugin package directory is missing or is a reparse point.");
        }

        _frameworkAssemblies = GetFrameworkAssemblyNames();
        _sharedAssemblies = (sharedAssemblies ?? [])
            .ToDictionary(
                static assembly => assembly.GetName().Name
                    ?? throw new ArgumentException("A shared assembly has no simple name."),
                StringComparer.OrdinalIgnoreCase);

        var managed = new Dictionary<string, DependencyFile>(
            StringComparer.OrdinalIgnoreCase);
        var native = new Dictionary<string, DependencyFile>(
            StringComparer.OrdinalIgnoreCase);
        var declared = dependencies.ToArray();
        if (declared.Length > PluginBrokerProtocol.MaximumDependencyCount)
        {
            throw new InvalidDataException(
                "The dependency allowlist exceeds its item limit.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var dependency in declared)
        {
            ArgumentNullException.ThrowIfNull(dependency);
            var file = CreateDependencyFile(dependency);
            if (!paths.Add(file.Path))
            {
                throw new InvalidDataException(
                    $"Dependency path '{dependency.RelativePath}' is duplicated.");
            }

            totalBytes = checked(totalBytes + new FileInfo(file.Path).Length);
            if (totalBytes > PluginBrokerProtocol.MaximumDependencySetBytes)
            {
                throw new InvalidDataException(
                    "The dependency allowlist exceeds its total size limit.");
            }

            VerifyDependency(file);
            if (string.Equals(dependency.Kind, "managed", StringComparison.Ordinal))
            {
                var assemblyName = AssemblyName.GetAssemblyName(file.Path);
                var simpleName = assemblyName.Name
                    ?? throw new InvalidDataException(
                        $"Managed dependency '{dependency.RelativePath}' has no simple name.");
                if (_frameworkAssemblies.Contains(simpleName) ||
                    _sharedAssemblies.ContainsKey(simpleName))
                {
                    throw new InvalidDataException(
                        $"Managed dependency name '{simpleName}' collides with a trusted assembly.");
                }

                if (!managed.TryAdd(simpleName, file with { AssemblyName = assemblyName }))
                {
                    throw new InvalidDataException(
                        $"Managed dependency name '{simpleName}' is duplicated.");
                }
            }
            else if (string.Equals(dependency.Kind, "native", StringComparison.Ordinal))
            {
                foreach (var name in GetNativeNames(file.Path))
                {
                    if (!native.TryAdd(name, file))
                    {
                        throw new InvalidDataException(
                            $"Native dependency name '{name}' is duplicated.");
                    }
                }
            }
            else
            {
                throw new InvalidDataException(
                    $"Dependency '{dependency.RelativePath}' has an unsupported kind.");
            }
        }

        _managedDependencies = managed;
        _nativeDependencies = native;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        var simpleName = assemblyName.Name
            ?? throw new FileLoadException("The requested assembly has no simple name.");

        if (_sharedAssemblies.TryGetValue(simpleName, out var shared))
        {
            if (!AssemblyName.ReferenceMatchesDefinition(assemblyName, shared.GetName()))
            {
                throw new FileLoadException(
                    $"Shared assembly '{assemblyName}' does not match the trusted definition.");
            }

            return shared;
        }

        if (_frameworkAssemblies.Contains(simpleName))
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }

        if (!_managedDependencies.TryGetValue(simpleName, out var dependency) ||
            dependency.AssemblyName is null ||
            !AssemblyName.ReferenceMatchesDefinition(assemblyName, dependency.AssemblyName))
        {
            throw new FileNotFoundException(
                $"Managed dependency '{assemblyName}' is not declared by the plugin manifest.");
        }

        using var stream = OpenVerifiedDependency(dependency);
        return LoadFromStream(stream);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unmanagedDllName);
        foreach (var name in GetNativeRequestNames(unmanagedDllName))
        {
            if (!_nativeDependencies.TryGetValue(name, out var dependency))
            {
                continue;
            }

            VerifyDependency(dependency);
            throw new DllNotFoundException(
                $"Native dependency '{unmanagedDllName}' is declared, but native activation is disabled.");
        }

        throw new DllNotFoundException(
            $"Native dependency '{unmanagedDllName}' is not declared by the plugin manifest.");
    }

    public void Dispose() => Unload();

    internal string ResolveManagedPathForTest(AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name
            ?? throw new FileLoadException("The requested assembly has no simple name.");
        if (!_managedDependencies.TryGetValue(simpleName, out var dependency) ||
            dependency.AssemblyName is null ||
            !AssemblyName.ReferenceMatchesDefinition(assemblyName, dependency.AssemblyName))
        {
            throw new FileNotFoundException(
                $"Managed dependency '{assemblyName}' is not declared by the plugin manifest.");
        }

        VerifyDependency(dependency);
        return dependency.Path;
    }

    internal string ResolveNativePathForTest(string unmanagedDllName)
    {
        foreach (var name in GetNativeRequestNames(unmanagedDllName))
        {
            if (_nativeDependencies.TryGetValue(name, out var dependency))
            {
                VerifyDependency(dependency);
                return dependency.Path;
            }
        }

        throw new DllNotFoundException(
            $"Native dependency '{unmanagedDllName}' is not declared by the plugin manifest.");
    }

    private DependencyFile CreateDependencyFile(PluginBrokerDependency dependency)
    {
        if (!PluginBrokerProtocol.IsValidSha256(dependency.Sha256) ||
            !IsCanonicalRelativeDependencyPath(dependency.RelativePath))
        {
            throw new InvalidDataException(
                $"Dependency '{dependency.RelativePath}' is not a valid allowlist entry.");
        }

        var path = Path.GetFullPath(
            Path.Combine(_packageDirectory, dependency.RelativePath));
        var packagePrefix = _packageDirectory + Path.DirectorySeparatorChar;
        if (!path.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) ||
            HasReparsePoint(path))
        {
            throw new InvalidDataException(
                $"Dependency '{dependency.RelativePath}' is outside the package or missing.");
        }

        return new DependencyFile(path, dependency.Sha256.ToUpperInvariant(), null);
    }

    private static bool IsCanonicalRelativeDependencyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > PluginBrokerProtocol.MaximumDependencyPathCharacters ||
            Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.None);
        return segments.All(static segment =>
            segment.Length > 0 &&
            segment is not "." and not ".." &&
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    private static HashSet<string> GetFrameworkAssemblyNames()
    {
        var runtimeDirectory = Path.TrimEndingDirectorySeparator(
            RuntimeEnvironment.GetRuntimeDirectory());
        return Directory.EnumerateFiles(runtimeDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    private static IEnumerable<string> GetNativeNames(string path)
    {
        var fileName = Path.GetFileName(path);
        yield return fileName;
        yield return Path.GetFileNameWithoutExtension(fileName);
    }

    private static IEnumerable<string> GetNativeRequestNames(string name)
    {
        var fileName = Path.GetFileName(name);
        yield return fileName;
        if (Path.HasExtension(fileName))
        {
            yield return Path.GetFileNameWithoutExtension(fileName);
        }
        else
        {
            yield return fileName + ".dll";
        }
    }

    private void VerifyDependency(DependencyFile dependency)
    {
        using var stream = OpenVerifiedDependency(dependency);
    }

    private FileStream OpenVerifiedDependency(DependencyFile dependency)
    {
        if (!File.Exists(dependency.Path) || HasReparsePoint(dependency.Path))
        {
            throw new FileLoadException(
                $"Dependency '{dependency.Path}' is missing or became a reparse point.");
        }

        var info = new FileInfo(dependency.Path);
        if (info.Length is <= 0 or > PluginBrokerProtocol.MaximumDependencyBytes)
        {
            throw new FileLoadException(
                $"Dependency '{dependency.Path}' is outside the size limit.");
        }

        var stream = new FileStream(
            dependency.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        try
        {
            var observedHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(
                    observedHash,
                    dependency.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FileLoadException(
                    $"Dependency '{dependency.Path}' changed after the grant was issued.");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private bool HasReparsePoint(string path)
    {
        if ((File.GetAttributes(_packageDirectory) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        for (var current = Path.GetDirectoryName(path);
             current is not null &&
             !string.Equals(current, _packageDirectory, StringComparison.OrdinalIgnoreCase);
             current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record DependencyFile(
        string Path,
        string Sha256,
        AssemblyName? AssemblyName);
}
