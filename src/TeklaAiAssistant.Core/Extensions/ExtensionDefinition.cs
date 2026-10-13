namespace TeklaAiAssistant.Core.Extensions;

public sealed record ExtensionDefinition(
    string Name,
    string RootPath,
    IReadOnlyList<BundleDefinition> Bundles);

public sealed record BundleDefinition(
    BundleKind Kind,
    string Name,
    string Path,
    string? CommandAssembly,
    string? CommandType,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<BundleDefinition> Children)
{
    public TeklaAiAssistant.Core.Commands.BundleInfo? Command { get; init; }
}
