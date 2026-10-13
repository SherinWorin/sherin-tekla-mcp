namespace TeklaAiAssistant.Core.Contracts;

public readonly record struct Point3(double X, double Y, double Z);

public readonly record struct Vector3(double X, double Y, double Z);

public sealed record LineSegment(Point3 Start, Point3 End);

public sealed record BoundingBox(Point3 Minimum, Point3 Maximum);
