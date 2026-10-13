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
    ReadSelectionResult ReadSelection();
}

public interface ILogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed record Point3DResult(double Xmm, double Ymm, double Zmm);

public sealed record UdaValueResult(
    string Name,
    string ConfiguredType,
    bool Found,
    object? Value);

public sealed record SelectedObjectResult(
    int Id,
    string? Guid,
    string ObjectType,
    string? Name,
    string? Profile,
    string? Material,
    string? Class,
    string? PartPosition,
    string? AssemblyPosition,
    int? AssemblyId,
    string? AssemblyGuid,
    string? BeamType,
    Point3DResult? StartPoint,
    Point3DResult? EndPoint,
    IReadOnlyList<Point3DResult>? ContourPoints,
    double? LengthMm,
    double? WeightKg,
    IReadOnlyDictionary<string, UdaValueResult> Udas);

public sealed record ReadSelectionResult(
    string ModelName,
    string ModelPath,
    int SelectedObjectCount,
    int ReportedPartCount,
    int SkippedNonPartCount,
    IReadOnlyList<SelectedObjectResult> Objects);

public sealed record CommandResult(bool Success, string Message, ReadSelectionResult Selection)
{
    public IReadOnlyList<SelectedObjectResult> Objects => Selection.Objects;

    public static CommandResult Failed(string message)
        => new(false, message, new ReadSelectionResult(
            string.Empty, string.Empty, 0, 0, 0, Array.Empty<SelectedObjectResult>()));
}

public sealed record BundleInfo(
    string CommandId,
    string Title,
    string Tooltip,
    string AssemblySimpleName,
    string AssemblyFileName,
    string CommandType,
    bool ReadOnly);
