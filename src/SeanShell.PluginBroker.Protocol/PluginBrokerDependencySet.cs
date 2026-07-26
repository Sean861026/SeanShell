using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SeanShell.PluginBroker.Protocol;

public static class PluginBrokerDependencySet
{
    public static string ComputeDigest(IEnumerable<PluginBrokerDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var dependency in dependencies.OrderBy(
                     static item => NormalizePath(item.RelativePath),
                     StringComparer.Ordinal))
        {
            Append(hash, NormalizePath(dependency.RelativePath));
            Append(hash, dependency.Kind.ToLowerInvariant());
            Append(hash, dependency.Sha256.ToUpperInvariant());
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').ToLowerInvariant();

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
