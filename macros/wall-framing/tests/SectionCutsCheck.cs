// self-check of WfSectionRules.SectionCuts (synthetic pods, no client data):
// four inside-out cuts always; one more per full-length partition, seen from the
// larger room; a ledge-wall cut when there is no partition.
using System; using System.Collections.Generic; using P142.WallFraming;
using WL = P142.WallFraming.WfSectionRules.WallLine;
class C {
  static int f = 0;
  static void T(bool c, string m) { Console.WriteLine((c ? "PASS " : "FAIL ") + m); if (!c) f++; }
  static string Sig(List<WfSectionRules.Cut> cs) {
    var l = new List<string>();
    foreach (var c in cs) {
      string look = WfSectionRules.LookOf(c.LeftX, c.LeftY, c.RightX, c.RightY);
      if (look != (c.Look > 0 ? "+" : "-") + c.Axis) return "BAD MARK " + c.Name;
      l.Add(c.Name + look + "@" + c.Target);
    }
    return string.Join(" ", l.ToArray()); }
  static List<WL> Box() { return new List<WL> {
      new WL { X0 = 50, Y0 = 0, X1 = 50, Y1 = 2800 }, new WL { X0 = 2950, Y0 = 0, X1 = 2950, Y1 = 2800 },
      new WL { X0 = 0, Y0 = 50, X1 = 3000, Y1 = 50 }, new WL { X0 = 0, Y0 = 2750, X1 = 3000, Y1 = 2750 } }; }
  static int Main() {
    var a = WfSectionRules.SectionCuts(Box(), 0, 0, 3000, 2800);
    T(Sig(a) == "S1+x@2950 S2-x@50 S3-y@50 S4+y@2750", "plain pod: four cuts " + Sig(a));
    foreach (var c in a) T(c.Ahead >= 150 && c.Ahead <= 900, c.Name + " mark line in front of its wall (" + c.Ahead + ")");
    var w = Box(); w.Add(new WL { X0 = 1700, Y0 = 50, X1 = 1700, Y1 = 2750 });
    var b = WfSectionRules.SectionCuts(w, 0, 0, 3000, 2800);
    T(Sig(b) == "S1+x@2950 S2-x@50 S3-y@50 S4+y@2750 S5+x@1700", "partition: seen from the larger room " + Sig(b));
    T(b[4].At < 1700 && b[4].At > 1350, "partition cut 150..300 in front (" + b[4].At + ")");
    var s = Box(); s.Add(new WL { X0 = 1700, Y0 = 50, X1 = 1700, Y1 = 900 });
    T(WfSectionRules.SectionCuts(s, 0, 0, 3000, 2800).Count == 4, "short inner wall: no extra cut");
    var l = Box(); l.Add(new WL { X0 = 1500, Y0 = 50, X1 = 1500, Y1 = 1200 });     // inner stub (not full length)
    l.Add(new WL { X0 = 600, Y0 = 2700, X1 = 600, Y1 = 2400, Ledge = true }); l.Add(new WL { X0 = 1000, Y0 = 2700, X1 = 1000, Y1 = 2400, Ledge = true });
    var d = WfSectionRules.SectionCuts(l, 0, 0, 3000, 2800);
    T(d.Count == 5 && d[4].Axis == 'x' && d[4].Look == +1 && d[4].At == 800 && d[4].Target == 1500, "ledge cut on the ledge middle, toward the inner wall " + Sig(d));
    Console.WriteLine(f == 0 ? "SECTION CUTS CHECK PASSED" : "SECTION CUTS CHECK FAILED"); return f; } }
