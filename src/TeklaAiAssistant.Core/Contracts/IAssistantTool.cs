namespace TeklaAiAssistant.Core.Contracts;

public sealed record ToolRequest(
    string RequestId,
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments,
    IReadOnlyList<ObjectIdentity>? Scope = null,
    bool PreviewOnly = true,
    string? ExpectedSnapshotId = null);

public interface IAssistantTool
{
    string Name { get; }

    bool IsReadOnly { get; }

    Task<ToolResult<object?>> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default);
}
