namespace TeklaAiAssistant.Core.Contracts;

public sealed record ModelSnapshot(
    string SnapshotId,
    DateTimeOffset CapturedAt,
    IReadOnlyList<ObjectIdentity> Objects,
    string? SourceRevision = null);

public sealed record ObjectSnapshot(
    ObjectIdentity Identity,
    IReadOnlyList<PropertyValue> Properties,
    IReadOnlyList<LineSegment> Segments,
    BoundingBox? Bounds = null);
