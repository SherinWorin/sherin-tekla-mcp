using System;
using System.Globalization;

namespace SherinTeklaMcp.Core.Geometry
{
    /// <summary>
    /// A 3D point in millimetres. Every point this server reads or writes is in the
    /// Tekla GLOBAL coordinate system (the Tekla layer switches to the global work plane
    /// before touching geometry and restores the user's plane afterwards).
    /// </summary>
    public sealed class Point3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Point3() { }

        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double DistanceTo(Point3 other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            var dx = other.X - X;
            var dy = other.Y - Y;
            var dz = other.Z - Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public bool IsFinite() => IsFinite(X) && IsFinite(Y) && IsFinite(Z);

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>Parses "x,y,z" (mm, invariant culture, spaces allowed). Returns null when invalid.</summary>
        public static Point3? TryParse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text!.Split(',');
            if (parts.Length != 3) return null;
            var values = new double[3];
            for (var i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return null;
            }
            var p = new Point3(values[0], values[1], values[2]);
            return p.IsFinite() ? p : null;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}", X, Y, Z);
    }
}
