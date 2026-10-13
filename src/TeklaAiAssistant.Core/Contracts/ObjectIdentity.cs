namespace TeklaAiAssistant.Core.Contracts;

public sealed record ObjectIdentity(
    string StableId,
    string? Kind = null,
    string? DisplayName = null);
