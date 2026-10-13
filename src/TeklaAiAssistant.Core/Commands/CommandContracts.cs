namespace TeklaAiAssistant.Core.Commands;

public interface ITeklaCommand
{
    string Id { get; }
    CommandResult Execute(ICommandContext context);
}

public interface ICommandContext
{
    ITeklaSession Session { get; }
    ILogger Log { get; }
    BundleInfo Bundle { get; }
}

/// <summary>A read-only boundary. Implementations never expose Tekla vendor objects.</summary>
public interface ITeklaSession
{
    bool IsConnected { get; }
    IReadOnlyList<SelectedPartInfo> GetSelectedParts();
}

public interface ILogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed record SelectedPartInfo(
    string Id,
    string Profile,
    string Material,
    double? LengthMm,
    string PartPosition,
    string AssemblyPosition);

public sealed record CommandResult(bool Success, string Message, IReadOnlyList<SelectedPartInfo> Parts)
{
    public static CommandResult Failed(string message)
        => new(false, message, Array.Empty<SelectedPartInfo>());
}

public sealed record BundleInfo(
    string CommandId,
    string Title,
    string Tooltip,
    string AssemblySimpleName,
    string AssemblyFileName,
    string CommandType,
    bool ReadOnly);
