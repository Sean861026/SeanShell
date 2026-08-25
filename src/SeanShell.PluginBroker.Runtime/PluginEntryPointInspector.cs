using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SeanShell.PluginBroker.Protocol;

namespace SeanShell.PluginBroker.Runtime;

public static class PluginEntryPointInspector
{
    private const string ContractNamespace = "SeanShell.PluginContracts";
    private const string ContractInterface = "ISeanShellPlugin";

    public static string? Validate(string assemblyPath, string entryType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        if (!PluginBrokerActivationContract.IsValidEntryType(entryType))
        {
            return "The plugin entry type is outside the bounded activation contract.";
        }

        var path = Path.GetFullPath(assemblyPath);
        if (!File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return "The plugin entry assembly is unavailable.";
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                return "The plugin entry assembly does not contain managed metadata.";
            }

            var reader = peReader.GetMetadataReader();
            var matches = reader.TypeDefinitions
                .Where(handle => GetFullName(reader, handle) == entryType)
                .ToArray();
            if (matches.Length != 1)
            {
                return "The plugin entry type was not found exactly once in the entry assembly.";
            }

            var definition = reader.GetTypeDefinition(matches[0]);
            var attributes = definition.Attributes;
            if ((attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public ||
                (attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0)
            {
                return "The plugin entry type must be a public, non-abstract class.";
            }

            if (!definition.GetInterfaceImplementations().Any(handle =>
                    IsPluginContract(reader, reader.GetInterfaceImplementation(handle).Interface)))
            {
                return "The plugin entry type must directly implement ISeanShellPlugin.";
            }

            if (!definition.GetMethods().Any(handle =>
                    IsPublicParameterlessConstructor(reader, handle)))
            {
                return "The plugin entry type must expose a public parameterless constructor.";
            }

            return null;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or
                IOException or
                UnauthorizedAccessException)
        {
            return "The plugin entry assembly metadata is unreadable.";
        }
    }

    private static string GetFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var typeNamespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
    }

    private static bool IsPluginContract(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
            return reader.StringComparer.Equals(reference.Namespace, ContractNamespace) &&
                   reader.StringComparer.Equals(reference.Name, ContractInterface) &&
                   IsContractAssembly(reader, reference.ResolutionScope);
        }

        return false;
    }

    private static bool IsContractAssembly(MetadataReader reader, EntityHandle scope)
    {
        if (scope.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
        return reader.StringComparer.Equals(assembly.Name, "SeanShell.PluginContracts");
    }

    private static bool IsPublicParameterlessConstructor(
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        if (!reader.StringComparer.Equals(method.Name, ".ctor") ||
            (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public ||
            (method.Attributes & MethodAttributes.Static) != 0)
        {
            return false;
        }

        var signature = reader.GetBlobReader(method.Signature);
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            signature.ReadCompressedInteger();
        }

        return signature.ReadCompressedInteger() == 0;
    }
}
