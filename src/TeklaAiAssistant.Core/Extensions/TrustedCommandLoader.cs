using System.Reflection;
using System.Security.Cryptography;
using TeklaAiAssistant.Core.Commands;

namespace TeklaAiAssistant.Core.Extensions;

public sealed record TrustedCommandAllowlistEntry(
    string AssemblySimpleName,
    string Sha256,
    string CommandType,
    string CommandId);

public sealed record CommandLoadResult(bool Success, ITeklaCommand? Command, string Message);

/// <summary>
/// Loads explicitly trusted in-process code. This is NOT a security sandbox: ReadOnly is
/// a declared capability and all DLL dependencies must also be trusted by the deployer.
/// </summary>
public sealed class TrustedCommandLoader
{
    private static readonly object ReflectionLock = new();
    private readonly TrustedCommandAllowlistEntry[] allowlist;

    public TrustedCommandLoader(IEnumerable<TrustedCommandAllowlistEntry> allowlist)
    {
        if (allowlist == null) throw new ArgumentNullException(nameof(allowlist));
        this.allowlist = allowlist.ToArray();
        foreach (var entry in this.allowlist)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.AssemblySimpleName)
                || string.IsNullOrWhiteSpace(entry.CommandType) || string.IsNullOrWhiteSpace(entry.CommandId)
                || entry.Sha256 == null || entry.Sha256.Length != 64 || !entry.Sha256.All(IsHex))
                throw new ArgumentException("Every allowlist entry requires a simple name, command type/id and 64-character SHA256.", nameof(allowlist));
        }
    }

    public CommandLoadResult Load(BundleDefinition bundle)
    {
        if (bundle == null) return Failure("Bundle is missing.");
        if (bundle.Kind != BundleKind.PushButton || bundle.Command == null)
            return Failure("Bundle has no validated command metadata.");
        return Load(bundle.Command, bundle.Path);
    }

    public CommandLoadResult Load(BundleInfo bundle, string bundleDirectory)
    {
        try
        {
            if (bundle == null) return Failure("Bundle is missing.");
            if (!bundle.ReadOnly) return Failure("Only readOnly=true commands are supported by this MVP.");
            if (string.IsNullOrWhiteSpace(bundle.AssemblyFileName)
                || bundle.AssemblyFileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || !bundle.AssemblyFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return Failure("Command assembly must be a local DLL filename.");

            var matching = allowlist.Where(entry => entry.AssemblySimpleName == bundle.AssemblySimpleName
                && entry.CommandType == bundle.CommandType && entry.CommandId == bundle.CommandId).ToArray();
            if (matching.Length == 0) return Failure("Command is not explicitly allowlisted.");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(bundleDirectory, bundle.AssemblyFileName));

            // Windows/net48 FileShare.Read denies replacement/writes throughout validation and load.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length == 0 || file.Length > int.MaxValue) return Failure("Command DLL has an invalid size.");
            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                file.CopyTo(buffer);
                bytes = buffer.ToArray();
            }
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            if (!matching.Any(entry => string.Equals(entry.Sha256, hash, StringComparison.OrdinalIgnoreCase)))
                return Failure("Command DLL SHA256 is not allowlisted.");

            AssemblyName identity;
            lock (ReflectionLock)
            {
                // Reflection-only loading inspects approved bytes without running command code.
                var metadataAssembly = Assembly.ReflectionOnlyLoad(bytes);
                identity = metadataAssembly.GetName();
                if (!string.Equals(identity.Name, bundle.AssemblySimpleName, StringComparison.Ordinal))
                    return Failure("Command DLL assembly simple name does not match metadata/allowlist.");

                ResolveEventHandler resolver = ResolveContractMetadata;
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolver;
                try
                {
                    var type = metadataAssembly.GetType(bundle.CommandType, false, false);
                    if (type == null || !type.IsClass || !type.IsVisible || type.IsAbstract || type.ContainsGenericParameters
                        || type.GetConstructor(Type.EmptyTypes) == null)
                        return Failure("Command type must be a public concrete class with a public parameterless constructor.");
                    if (!type.GetInterfaces().Any(contract => contract.FullName == typeof(ITeklaCommand).FullName
                        && contract.Assembly.FullName == typeof(ITeklaCommand).Assembly.FullName))
                        return Failure("Command type does not implement the host ITeklaCommand contract.");
                }
                finally
                {
                    AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= resolver;
                }
            }

            // LoadFrom can unify identities with assemblies already in the AppDomain; refuse those
            // unless their bytes were already explicitly approved for this same command identity.
            foreach (var existing in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.FullName == identity.FullName))
            {
                if (existing.IsDynamic || string.IsNullOrEmpty(existing.Location)
                    || !string.Equals(HashFile(existing.Location), hash, StringComparison.OrdinalIgnoreCase))
                    return Failure("An assembly with this identity is already loaded with different or unverifiable bytes.");
            }
            var assembly = Assembly.LoadFrom(path);
            if (assembly.FullName != identity.FullName
                || !string.Equals(HashFile(assembly.Location), hash, StringComparison.OrdinalIgnoreCase))
                return Failure("Loaded command assembly did not match the validated DLL.");
            var runtimeType = assembly.GetType(bundle.CommandType, true, false)!;
            if (!typeof(ITeklaCommand).IsAssignableFrom(runtimeType))
                return Failure("Loaded command uses an incompatible ITeklaCommand contract.");
            var command = (ITeklaCommand)Activator.CreateInstance(runtimeType)!;
            if (!string.Equals(command.Id, bundle.CommandId, StringComparison.Ordinal))
                return Failure("Command Id does not match metadata/allowlist.");
            return new CommandLoadResult(true, command, "Trusted command loaded.");
        }
        catch (Exception exception)
        {
            return Failure("Command load failed: " + exception.GetBaseException().Message);
        }
    }

    private static Assembly? ResolveContractMetadata(object sender, ResolveEventArgs args)
    {
        // Never discover arbitrary dependency DLLs while validating the command type.
        if (args.Name != typeof(ITeklaCommand).Assembly.FullName) return null;
        return AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies().FirstOrDefault(a => a.FullName == args.Name)
            ?? Assembly.ReflectionOnlyLoadFrom(typeof(ITeklaCommand).Assembly.Location);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    private static bool IsHex(char value)
        => (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');

    private static CommandLoadResult Failure(string message) => new(false, null, message);
}
