using System;

namespace SherinTeklaMcp.Core.Geometry
{
    /// <summary>Small, deterministic geometry helpers. The AI asks for these through tools
    /// instead of doing engineering geometry in natural language.</summary>
    public static class GeometryMath
    {
        /// <summary>Default tolerance for "same point" comparisons, in mm.</summary>
        public const double PointToleranceMm = 0.5;

        public static double Length(Point3 start, Point3 end) => start.DistanceTo(end);

        public static Point3 Midpoint(Point3 a, Point3 b) =>
            new Point3((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);

        /// <summary>Angle between the member axis and the horizontal XY plane, in degrees (0..90).</summary>
        public static double AngleToHorizontalDeg(Point3 start, Point3 end)
        {
            var dz = Math.Abs(end.Z - start.Z);
            var horizontal = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (dz < 1e-9 && horizontal < 1e-9) return 0;
            return Math.Atan2(dz, horizontal) * 180.0 / Math.PI;
        }

        /// <summary>True when the member axis is within <paramref name="toleranceDeg"/> of vertical.</summary>
        public static bool IsVertical(Point3 start, Point3 end, double toleranceDeg = 1.0) =>
            start.DistanceTo(end) > 1e-9 && AngleToHorizontalDeg(start, end) >= 90.0 - toleranceDeg;

        public static bool SamePoint(Point3 a, Point3 b, double toleranceMm = PointToleranceMm) =>
            a.DistanceTo(b) <= toleranceMm;

        /// <summary>Same member line in either direction (start/end may be swapped).</summary>
        public static bool SameLine(Point3 a1, Point3 a2, Point3 b1, Point3 b2, double toleranceMm = PointToleranceMm) =>
            (SamePoint(a1, b1, toleranceMm) && SamePoint(a2, b2, toleranceMm)) ||
            (SamePoint(a1, b2, toleranceMm) && SamePoint(a2, b1, toleranceMm));
    }
}
