using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using SeanShell.PluginBroker.Protocol;
using SeanShell.PluginBroker.Runtime;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PluginDependencyLoadContextTests
{
    [TestMethod]
    public void DeclaredManagedDependencyResolvesToExactPackageFile()
    {
        using var package = new TemporaryResolverPackage();
        var dependency = package.AddManagedDependency(
            typeof(PluginBrokerProtocol).Assembly.Location,
            "lib\\BrokerProtocol.dll");
        using var context = new PluginDependencyLoadContext(
            package.DirectoryPath,
            [dependency]);

        var path = context.ResolveManagedPathForTest(
            typeof(PluginBrokerProtocol).Assembly.GetName());
        var loaded = context.LoadFromAssemblyName(
            typeof(PluginBrokerProtocol).Assembly.GetName());

        Assert.AreEqual(
            Path.Combine(package.DirectoryPath, dependency.RelativePath),
            path);
        Assert.AreEqual(context, AssemblyLoadContext.GetLoadContext(loaded));
        Assert.AreEqual(string.Empty, loaded.Location);
    }

    [TestMethod]
    public void UndeclaredManagedDependencyDoesNotFallBackToHost()
    {
        using var package = new TemporaryResolverPackage();
        using var context = new PluginDependencyLoadContext(
            package.DirectoryPath,
            []);

        var exception = Assert.ThrowsExactly<FileNotFoundException>(
            () => context.LoadFromAssemblyName(
                typeof(PluginBrokerProtocol).Assembly.GetName()));

        Assert.IsNotNull(exception);
        Assert.IsFalse(context.Assemblies.Any(
            assembly => string.Equals(
                assembly.GetName().Name,
                typeof(PluginBrokerProtocol).Assembly.GetName().Name,
                StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ExplicitSharedAssemblyUsesTrustedHostDefinition()
    {
        using var package = new TemporaryResolverPackage();
        var trusted = typeof(PluginBrokerProtocol).Assembly;
        using var context = new PluginDependencyLoadContext(
            package.DirectoryPath,
            [],
            [trusted]);

        var loaded = context.LoadFromAssemblyName(trusted.GetName());

        Assert.AreSame(trusted, loaded);
        Assert.AreEqual(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(loaded));
    }

    [TestMethod]
    public void DeclaredNativeDependencyAcceptsCanonicalNameVariants()
    {
        using var package = new TemporaryResolverPackage();
        var dependency = package.AddNativeDependency("native\\SeanNative.dll");
        using var context = new PluginDependencyLoadContext(
            package.DirectoryPath,
            [dependency]);

        var first = context.ResolveNativePathForTest("SeanNative");
        var second = context.ResolveNativePathForTest("SeanNative.dll");

        Assert.AreEqual(first, second);
        Assert.AreEqual(
            Path.Combine(package.DirectoryPath, dependency.RelativePath),
            first);
    }

    [TestMethod]
    public void UndeclaredNativeDependencyIsRejected()
    {
        using var package = new TemporaryResolverPackage();
        package.AddNativeDependency("native\\PresentButUndeclared.dll");
        using var context = new PluginDependencyLoadContext(
            package.DirectoryPath,
            []);

        var exception = Assert.ThrowsExactly<DllNotFoundException>(
            () => context.ResolveNativePathForTest("PresentButUndeclared"));

        StringAssert.Contains(exception.Message, "not declared");
    }

    [TestMethod]
    public void DependencyChangedAfterContextCreationIsRejected()
    {
        using var package = new TemporaryResolverPackage();
        var dependency = package.AddManagedDependency(
            typeof(PluginBrokerProtocol).Assembly.Location,
            "Support.dll");
        using var context = new PluginDependencyLoadContext(
            package.DirectoryPath,
            [dependency]);
        File.AppendAllText(
            Path.Combine(package.DirectoryPath, dependency.RelativePath),
            "tampered");

        var exception = Assert.ThrowsExactly<FileLoadException>(
            () => context.ResolveManagedPathForTest(
                typeof(PluginBrokerProtocol).Assembly.GetName()));

        StringAssert.Contains(exception.Message, "changed");
    }

    [TestMethod]
    public void DependencyHashMismatchIsRejectedBeforeContextIsUsable()
    {
        using var package = new TemporaryResolverPackage();
        var dependency = package.AddNativeDependency("Support.dll") with
        {
            Sha256 = new string('A', 64),
        };

        var exception = Assert.ThrowsExactly<FileLoadException>(
            () => new PluginDependencyLoadContext(
                package.DirectoryPath,
                [dependency]));

        StringAssert.Contains(exception.Message, "changed");
    }

    [TestMethod]
    public void DependencyOutsidePackageIsRejected()
    {
        using var package = new TemporaryResolverPackage();
        var dependency = new PluginBrokerDependency(
            "..\\Outside.dll",
            new string('A', 64),
            "native");

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => new PluginDependencyLoadContext(
                package.DirectoryPath,
                [dependency]));

        StringAssert.Contains(exception.Message, "valid allowlist");
    }

    [TestMethod]
    public void ManagedDependencyCannotShadowTrustedSharedAssembly()
    {
        using var package = new TemporaryResolverPackage();
        var trusted = typeof(PluginBrokerProtocol).Assembly;
        var dependency = package.AddManagedDependency(
            trusted.Location,
            "Shadow.dll");

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => new PluginDependencyLoadContext(
                package.DirectoryPath,
                [dependency],
                [trusted]));

        StringAssert.Contains(exception.Message, "collides");
    }

    [TestMethod]
    public void DependencyCountLimitIsEnforcedAgainAtLoadBoundary()
    {
        using var package = new TemporaryResolverPackage();
        var dependency = package.AddNativeDependency("Support.dll");
        var dependencies = Enumerable
            .Repeat(dependency, PluginBrokerProtocol.MaximumDependencyCount + 1)
            .ToArray();

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => new PluginDependencyLoadContext(
                package.DirectoryPath,
                dependencies));

        StringAssert.Contains(exception.Message, "item limit");
    }

    private sealed class TemporaryResolverPackage : IDisposable
    {
        public TemporaryResolverPackage()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"SeanShell.Resolver.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public PluginBrokerDependency AddManagedDependency(
            string sourcePath,
            string relativePath)
        {
            var destination = PreparePath(relativePath);
            File.Copy(sourcePath, destination);
            return CreateDependency(destination, relativePath, "managed");
        }

        public PluginBrokerDependency AddNativeDependency(string relativePath)
        {
            var destination = PreparePath(relativePath);
            File.WriteAllBytes(destination, [1, 2, 3, 4]);
            return CreateDependency(destination, relativePath, "native");
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }

        private string PreparePath(string relativePath)
        {
            var path = Path.Combine(DirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        private static PluginBrokerDependency CreateDependency(
            string path,
            string relativePath,
            string kind)
        {
            using var stream = File.OpenRead(path);
            return new PluginBrokerDependency(
                relativePath,
                Convert.ToHexString(SHA256.HashData(stream)),
                kind);
        }
    }
}
