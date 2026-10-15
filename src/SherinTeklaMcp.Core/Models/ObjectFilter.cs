using System;
using System.Collections.Generic;
using System.Linq;

namespace SherinTeklaMcp.Core.Models
{
    /// <summary>
    /// Structured search filter. The AI fills these fields; it never writes Tekla filter
    /// syntax. All string comparisons are case-insensitive. Empty fields are ignored.
    /// </summary>
    public sealed class ObjectFilter
    {
        /// <summary>Open API type names to include, e.g. Beam, PolyBeam, ContourPlate. Empty = all parts.</summary>
        public List<string> Types { get; set; } = new List<string>();
        public string ProfileEquals { get; set; } = "";
        public string ProfileContains { get; set; } = "";
        public string Material { get; set; } = "";
        public string Class { get; set; } = "";
        public string NameContains { get; set; } = "";
        /// <summary>"vertical" (columns), "nonvertical" (beams/braces) or empty.</summary>
        public string Orientation { get; set; } = "";

        public bool IsEmpty =>
            Types.Count == 0 && string.IsNullOrWhiteSpace(ProfileEquals) && string.IsNullOrWhiteSpace(ProfileContains) &&
            string.IsNullOrWhiteSpace(Material) && string.IsNullOrWhiteSpace(Class) &&
            string.IsNullOrWhiteSpace(NameContains) && string.IsNullOrWhiteSpace(Orientation);

        public bool Matches(TeklaObjectInfo o)
        {
            if (o is null) return false;
            if (Types.Count > 0 && !Types.Any(t => Eq(t, o.Type))) return false;
            if (!string.IsNullOrWhiteSpace(ProfileEquals) && !Eq(ProfileEquals, o.Profile)) return false;
            if (!string.IsNullOrWhiteSpace(ProfileContains) && !Contains(o.Profile, ProfileContains)) return false;
            if (!string.IsNullOrWhiteSpace(Material) && !Eq(Material, o.Material)) return false;
            if (!string.IsNullOrWhiteSpace(Class) && !Eq(Class, o.Class)) return false;
            if (!string.IsNullOrWhiteSpace(NameContains) && !Contains(o.Name, NameContains)) return false;
            if (!string.IsNullOrWhiteSpace(Orientation))
            {
                var o2 = Orientation.Trim().ToLowerInvariant();
                if (o2 == "vertical" && !o.IsVertical) return false;
                if (o2 == "nonvertical" && o.IsVertical) return false;
            }
            return true;
        }

        private static bool Eq(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string haystack, string needle) =>
            (haystack ?? "").IndexOf((needle ?? "").Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
