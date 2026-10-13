namespace TeklaAiAssistant.Core.Contracts;

public enum ToolResultStatus
{
    Succeeded,
    Preview,
    Rejected,
    Failed,
    Cancelled
}

public sealed record ToolResult<T>(
    string RequestId,
    string ToolName,
    ToolResultStatus Status,
    T? Data = default,
    IReadOnlyList<string>? Messages = null,
    IReadOnlyList<string>? Warnings = null);
