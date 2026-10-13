using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Serialization;
using TeklaAiAssistant.Core.Commands;

namespace TeklaAiAssistant.Core.Extensions;

public sealed record BundleDiscoveryError(string Path, string Message);
public sealed record ExtensionDiscoveryResult(
    IReadOnlyList<ExtensionDefinition> Extensions,
    IReadOnlyList<BundleDiscoveryError> Errors);

public sealed class ExtensionDiscoveryService
{
    /// <summary>Compatibility API. Use DiscoverWithErrors when diagnostics are needed.</summary>
    public IReadOnlyList<ExtensionDefinition> Discover(IEnumerable<string> roots)
        => DiscoverWithErrors(roots).Extensions;

    public ExtensionDiscoveryResult DiscoverWithErrors(IEnumerable<string> roots)
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        var extensions = new List<ExtensionDefinition>();
        var errors = new List<BundleDiscoveryError>();
        foreach (var root in roots)
        {
            foreach (var path in Directories(root, "*.extension", errors))
            {
                var bundles = Children(path, BundleKind.Tab, "*.tab", errors);
                extensions.Add(new ExtensionDefinition(System.IO.Path.GetFileName(path), path, bundles));
            }
        }
        return new ExtensionDiscoveryResult(extensions.ToArray(), errors.ToArray());
    }

    private static IReadOnlyList<BundleDefinition> Children(string parent, BundleKind kind, string pattern, List<BundleDiscoveryError> errors)
    {
        var bundles = new List<BundleDefinition>();
        foreach (var path in Directories(parent, pattern, errors))
        {
            try
            {
                var (metadata, command) = BundleMetadataReader.Read(path, kind);
                ValidateCommandTypeWhenAssemblyIsPresent(path, command, errors);
                var children = kind == BundleKind.Tab
                    ? Children(path, BundleKind.Panel, "*.panel", errors)
                    : kind == BundleKind.Panel
                        ? Children(path, BundleKind.PushButton, "*.pushbutton", errors)
                        : Array.Empty<BundleDefinition>();
                bundles.Add(new BundleDefinition(kind, System.IO.Path.GetFileName(path), path,
                    command?.AssemblyFileName, command?.CommandType, metadata, children) { Command = command });
            }
            catch (Exception exception)
            {
                errors.Add(new BundleDiscoveryError(path, exception.Message));
            }
        }
        return bundles.ToArray();
    }

    private static void ValidateCommandTypeWhenAssemblyIsPresent(string path, BundleInfo? command,
        List<BundleDiscoveryError> errors)
    {
        if (command == null) return;
        var assemblyPath = System.IO.Path.Combine(path, command.AssemblyFileName);
        if (!File.Exists(assemblyPath)) return;
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();
            var found = metadataReader.TypeDefinitions.Any(handle =>
            {
                var definition = metadataReader.GetTypeDefinition(handle);
                var @namespace = metadataReader.GetString(definition.Namespace);
                var name = metadataReader.GetString(definition.Name);
                var fullName = string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
                return string.Equals(fullName, command.CommandType, StringComparison.Ordinal);
            });
            if (!found)
                throw new SerializationException("Unknown command type: " + command.CommandType + ".");
        }
        catch (Exception exception)
        {
            errors.Add(new BundleDiscoveryError(path, exception.Message));
        }
    }

    private static string[] Directories(string root, string pattern, List<BundleDiscoveryError> errors)
    {
        try
        {
            if (!Directory.Exists(root)) return Array.Empty<string>();
            return Directory.GetDirectories(root, pattern).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
        catch (Exception exception)
        {
            errors.Add(new BundleDiscoveryError(root, exception.Message));
            return Array.Empty<string>();
        }
    }
}
