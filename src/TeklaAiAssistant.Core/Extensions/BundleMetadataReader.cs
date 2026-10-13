using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;
using TeklaAiAssistant.Core.Commands;

namespace TeklaAiAssistant.Core.Extensions;

internal static class BundleMetadataReader
{
    public static (IReadOnlyDictionary<string, string> Metadata, BundleInfo? Command) Read(string path, BundleKind kind)
    {
        var file = System.IO.Path.Combine(path, "bundle.json");
        // Group containers may omit metadata; executable bundles may not.
        if (!File.Exists(file) && kind != BundleKind.PushButton)
            return (new Dictionary<string, string>(), null);

        using var fileStream = File.OpenRead(file);
        if (fileStream.Length > 65536)
            throw new SerializationException("bundle.json exceeds the 64 KiB metadata limit.");
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            fileStream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        try
        {
            ValidateJson(bytes);
        }
        catch (XmlException exception)
        {
            throw new SerializationException("Invalid bundle.json: " + exception.Message, exception);
        }
        using var stream = new MemoryStream(bytes, false);
        var serializer = new DataContractJsonSerializer(typeof(MetadataDocument));
        var document = serializer.ReadObject(stream) as MetadataDocument
            ?? throw new SerializationException("bundle.json must contain a JSON object.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(values, "title", document.Title);
        Add(values, "tooltip", document.Tooltip);
        if (kind != BundleKind.PushButton)
            return (values, null);

        var id = Require(document.CommandId, "commandId");
        var title = Require(document.Title, "title");
        var tooltip = Require(document.Tooltip, "tooltip");
        var simpleName = Require(document.AssemblySimpleName, "assemblySimpleName");
        var filename = Require(document.CommandAssembly, "commandAssembly");
        var type = Require(document.CommandType, "commandType");
        if (!document.ReadOnly.HasValue)
            throw new SerializationException("Required metadata 'readOnly' is missing.");
        if (filename.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new SerializationException("commandAssembly must be a local DLL filename, without a path.");
        if (simpleName.IndexOfAny(new[] { '/', '\\', ':', ',' }) >= 0)
            throw new SerializationException("assemblySimpleName must be an assembly simple name.");

        values["commandId"] = id;
        values["assemblySimpleName"] = simpleName;
        values["commandAssembly"] = filename;
        values["commandType"] = type;
        values["readOnly"] = document.ReadOnly.Value.ToString(CultureInfo.InvariantCulture);
        return (values, new BundleInfo(id, title, tooltip, simpleName, filename, type, document.ReadOnly.Value));
    }

    private static string Require(string? value, string field)
    {
        var required = value;
        if (required == null || string.IsNullOrWhiteSpace(required))
            throw new SerializationException($"Required metadata '{field}' is missing, blank, or padded.");
        if (required != required.Trim())
            throw new SerializationException($"Required metadata '{field}' is missing, blank, or padded.");
        return required!;
    }

    private static void ValidateJson(byte[] bytes)
    {
        // The framework JSON reader preserves member names and JSON token types as XML,
        // allowing strict shape checks before DataContract deserialization/coercion.
        using var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max);
        var xml = new XmlDocument { XmlResolver = null };
        xml.Load(reader);
        var root = xml.DocumentElement;
        if (root == null || root.GetAttribute("type") != "object")
            throw new SerializationException("bundle.json must contain a JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlNode node in root.ChildNodes)
        {
            if (!(node is XmlElement member)) continue;
            var name = member.LocalName;
            if (!seen.Add(name)) throw new SerializationException("Duplicate metadata member: " + name);
            var expected = name == "readOnly" ? "boolean" : "string";
            if (name != "title" && name != "tooltip" && name != "commandId"
                && name != "assemblySimpleName" && name != "commandAssembly"
                && name != "commandType" && name != "readOnly")
                throw new SerializationException("Unknown metadata member: " + name);
            if (member.GetAttribute("type") != expected)
                throw new SerializationException("Metadata '" + name + "' must be a JSON " + expected + ".");
        }
    }

    private static void Add(IDictionary<string, string> values, string key, string? value)
    {
        if (value != null) values[key] = value;
    }

    [DataContract]
    private sealed class MetadataDocument
    {
        [DataMember(Name = "title")] public string? Title { get; set; }
        [DataMember(Name = "tooltip")] public string? Tooltip { get; set; }
        [DataMember(Name = "commandId")] public string? CommandId { get; set; }
        [DataMember(Name = "assemblySimpleName")] public string? AssemblySimpleName { get; set; }
        [DataMember(Name = "commandAssembly")] public string? CommandAssembly { get; set; }
        [DataMember(Name = "commandType")] public string? CommandType { get; set; }
        [DataMember(Name = "readOnly")] public bool? ReadOnly { get; set; }
    }
}
