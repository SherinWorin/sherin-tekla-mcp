namespace TeklaAiAssistant.Core.Contracts;

public sealed record PropertyValue(
    string Name,
    string? Value,
    string? DataType = null,
    string? Unit = null,
    bool IsAvailable = true);
